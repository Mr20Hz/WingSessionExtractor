# GUI workflow orchestrator

## Architecture overview

The desktop GUI is the explicit workflow host. It creates the workflow through
dependency injection, displays the configured steps, and starts one finite run
only after the user chooses input and output directories and presses **Start
Workflow**. There is no daemon, watcher, background agent, automatic startup,
or automatic application termination.

The orchestration contracts live in `WingSessionExtractor.Application` and do
not reference Avalonia:

- `IWorkflowStep` describes one ordered, cancellable unit of work with a stable
  technical ID and display name.
- `IWorkflowRunner` exposes step descriptors to its host and executes a
  `WorkflowContext`.
- `SequentialWorkflowRunner` orders enabled steps by order and then technical
  ID, runs them one at a time, and returns a structured `WorkflowResult`.
- `WorkflowContext` carries the selected directories, session output directory,
  extracted track paths, metadata, and run timestamps between steps.
- `WorkflowProgress`, `WorkflowStepResult`, and `WorkflowResult` carry progress
  and outcomes across the Application/GUI boundary.

The first configured step is `ExtractTracksWorkflowStep`. It invokes the
existing `ExportService` directly in-process; it never starts the CLI. Input
validation and output-directory creation remain in the existing session source
and channel exporter respectively. Keeping those operations together avoids a
second filesystem scan and avoids duplicating extraction preconditions merely
to display three artificial steps. For this version the selected output
directory is also the session directory.

## Lifecycle

The GUI moves through these states:

1. `Idle`: directory controls and Start are enabled when both paths are present.
2. `Running`: Start and directory controls are disabled; Cancel is enabled.
3. `Cancelling`: both buttons and directory controls are disabled while the
   active step cooperatively stops.
4. `Completed`, `Failed`, or `Cancelled`: Start and directory controls are
   enabled again, Cancel is disabled, and the final summary stays visible.

Each Start action creates a new `WorkflowContext` and cancellation token. The
runner records start/end timestamps, each attempted step's duration and result,
and the overall terminal state. A failed or cancelled step ends the run; later
steps are not called. Unexpected step exceptions become failed step results
that retain the original exception for diagnostics.

The application remains open after every terminal state. Automatic termination
is intentionally not part of this version.

## Cancellation behavior

Cancel requests cancellation on the token passed from the ViewModel through
the runner to the active step and existing extraction service. The session
source, exporter, and file-processing loops already check that token. The
exporter cleans up temporary `.partial` files before rethrowing cancellation.

An `OperationCanceledException` associated with the requested cancellation is
mapped to `Cancelled`, not `Failed`. Cancellation is cooperative, so the UI
shows **Cancelling…** until the active code reaches a cancellation check and the
runner returns its structured result.

## Progress model

Steps report a percentage from 0 through 100 and an optional status message.
The sequential runner gives each enabled step equal weight and calculates:

```text
overall = (completed step count + current step percentage / 100)
          / enabled step count * 100
```

The runner caps in-progress overall reports below 100. It emits overall 100
only after every enabled step has completed successfully. A step may report 100
while finalizing its own work; that does not make overall workflow progress
reach 100 early. Failed and cancelled runs retain their last reported progress.

`ExtractTracksWorkflowStep` maps the existing frame-based `ExportProgress` to
its step percentage and preserves the current WING session status message.

## Extending the workflow

A future step belongs in the Application layer (or an Application-facing
implementation assembly), depends on explicit services, and has no GUI types:

```csharp
public sealed class AnalyzeTracksWorkflowStep(ITrackAnalyzer analyzer)
    : IWorkflowStep
{
    public string Id => "analyze-tracks";
    public string DisplayName => "Analyze tracks";
    public int Order => 200;
    public bool IsEnabled => true;

    public async Task<WorkflowStepResult> ExecuteAsync(
        WorkflowContext context,
        IProgress<WorkflowStepProgress> progress,
        CancellationToken cancellationToken)
    {
        await analyzer.AnalyzeAsync(
            context.ExtractedTrackFiles,
            progress,
            cancellationToken);

        context.SetMetadata("analysis", "complete");
        return WorkflowStepResult.Success("Track analysis completed.");
    }
}
```

Register the implementation as another `IWorkflowStep` in the GUI composition
root. The ViewModel continues to depend only on `IWorkflowRunner`, so it needs
no change when steps are added. Step order and IDs should remain stable because
they are part of progress reporting and diagnostics.
