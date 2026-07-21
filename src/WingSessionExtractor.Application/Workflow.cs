using System.Diagnostics;
using System.Collections.ObjectModel;

namespace WingSessionExtractor.Application;

public enum WorkflowExecutionState
{
    Idle,
    Running,
    Cancelling,
    Completed,
    Failed,
    Cancelled
}

public sealed class WorkflowContext
{
    private readonly Dictionary<string, string> metadata =
        new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, string> readOnlyMetadata;
    private IReadOnlyList<string> extractedTrackFiles = Array.Empty<string>();

    public WorkflowContext(string inputDirectory, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        InputDirectory = inputDirectory;
        OutputDirectory = outputDirectory;
        SessionDirectory = outputDirectory;
        readOnlyMetadata = new ReadOnlyDictionary<string, string>(metadata);
    }

    public string InputDirectory { get; }

    public string OutputDirectory { get; }

    public string SessionDirectory { get; private set; }

    public IReadOnlyList<string> ExtractedTrackFiles => extractedTrackFiles;

    public DawProjectConfiguration? DawProjectConfiguration { get; private set; }

    public string? DawProjectPath { get; private set; }

    public IReadOnlyDictionary<string, string> Metadata => readOnlyMetadata;

    public DateTimeOffset? StartTime { get; private set; }

    public DateTimeOffset? EndTime { get; private set; }

    public void SetSessionDirectory(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        SessionDirectory = value;
    }

    public void SetExtractedTrackFiles(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        extractedTrackFiles = Array.AsReadOnly(files.ToArray());
    }

    public void ConfigureDawProject(DawProjectConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        DawProjectConfiguration = configuration;
    }

    public void SetDawProjectPath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        DawProjectPath = value;
    }

    public void SetMetadata(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        metadata[key] = value;
    }

    internal void Begin(DateTimeOffset startTime)
    {
        StartTime = startTime;
        EndTime = null;
    }

    internal void End(DateTimeOffset endTime) => EndTime = endTime;
}

public sealed record WorkflowStepProgress(
    double Percentage,
    string Message = "");

public sealed record WorkflowProgress(
    string CurrentStepId,
    string CurrentStepDisplayName,
    int CurrentStepIndex,
    int StepCount,
    double OverallPercentage,
    double StepPercentage,
    string Message);

public sealed record WorkflowStepResult(
    string StepId,
    string StepDisplayName,
    WorkflowExecutionState State,
    TimeSpan Duration,
    string Message,
    Exception? Exception = null)
{
    public static WorkflowStepResult Success(string message = "") =>
        new("", "", WorkflowExecutionState.Completed, TimeSpan.Zero, message);

    public static WorkflowStepResult Failure(
        string message,
        Exception? exception = null) =>
        new(
            "",
            "",
            WorkflowExecutionState.Failed,
            TimeSpan.Zero,
            message,
            exception);

    public static WorkflowStepResult Cancelled(string message = "Cancelled.") =>
        new("", "", WorkflowExecutionState.Cancelled, TimeSpan.Zero, message);
}

public sealed record WorkflowResult(
    WorkflowExecutionState State,
    WorkflowContext Context,
    IReadOnlyList<WorkflowStepResult> StepResults,
    TimeSpan Duration,
    string Message)
{
    public bool IsSuccessful => State == WorkflowExecutionState.Completed;
}

public sealed record WorkflowStepDescriptor(
    string Id,
    string DisplayName,
    int Order,
    bool IsEnabled);

public interface IWorkflowStep
{
    string Id { get; }

    string DisplayName { get; }

    int Order { get; }

    bool IsEnabled { get; }

    Task<WorkflowStepResult> ExecuteAsync(
        WorkflowContext context,
        IProgress<WorkflowStepProgress> progress,
        CancellationToken cancellationToken);
}

public interface IWorkflowRunner
{
    IReadOnlyList<WorkflowStepDescriptor> Steps { get; }

    Task<WorkflowResult> RunAsync(
        WorkflowContext context,
        IProgress<WorkflowProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class SequentialWorkflowRunner : IWorkflowRunner
{
    private const double MaximumInProgressPercentage = 99.999;
    private readonly IReadOnlyList<IWorkflowStep> steps;

    public SequentialWorkflowRunner(IEnumerable<IWorkflowStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        this.steps = steps
            .OrderBy(step => step.Order)
            .ThenBy(step => step.Id, StringComparer.Ordinal)
            .ToArray();

        var duplicateId = this.steps
            .GroupBy(step => step.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new ArgumentException(
                $"Workflow step ID must be unique: {duplicateId.Key}",
                nameof(steps));
        }

        Steps = this.steps
            .Select(step => new WorkflowStepDescriptor(
                step.Id,
                step.DisplayName,
                step.Order,
                step.IsEnabled))
            .ToArray();
    }

    public IReadOnlyList<WorkflowStepDescriptor> Steps { get; }

    public async Task<WorkflowResult> RunAsync(
        WorkflowContext context,
        IProgress<WorkflowProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var enabledSteps = steps.Where(step => step.IsEnabled).ToArray();
        var stepResults = new List<WorkflowStepResult>(enabledSteps.Length);
        var startedAt = DateTimeOffset.UtcNow;
        context.Begin(startedAt);

        for (var index = 0; index < enabledSteps.Length; index++)
        {
            var step = enabledSteps[index];
            var stopwatch = Stopwatch.StartNew();

            Report(
                progress,
                step,
                index,
                enabledSteps.Length,
                0,
                $"Starting {step.DisplayName}.");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stepProgress = new InlineProgress<WorkflowStepProgress>(item =>
                    Report(
                        progress,
                        step,
                        index,
                        enabledSteps.Length,
                        item.Percentage,
                        item.Message));

                var result = await step.ExecuteAsync(
                    context,
                    stepProgress,
                    cancellationToken);
                stopwatch.Stop();

                var normalized = result with
                {
                    StepId = step.Id,
                    StepDisplayName = step.DisplayName,
                    Duration = stopwatch.Elapsed
                };
                stepResults.Add(normalized);

                if (normalized.State == WorkflowExecutionState.Cancelled)
                {
                    return Finish(
                        WorkflowExecutionState.Cancelled,
                        context,
                        stepResults,
                        startedAt,
                        normalized.Message);
                }

                if (normalized.State != WorkflowExecutionState.Completed)
                {
                    return Finish(
                        WorkflowExecutionState.Failed,
                        context,
                        stepResults,
                        startedAt,
                        normalized.Message);
                }

                if (index < enabledSteps.Length - 1)
                {
                    Report(
                        progress,
                        step,
                        index,
                        enabledSteps.Length,
                        100,
                        normalized.Message);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                stepResults.Add(new WorkflowStepResult(
                    step.Id,
                    step.DisplayName,
                    WorkflowExecutionState.Cancelled,
                    stopwatch.Elapsed,
                    "Cancelled."));

                return Finish(
                    WorkflowExecutionState.Cancelled,
                    context,
                    stepResults,
                    startedAt,
                    "Workflow cancelled.");
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                stepResults.Add(new WorkflowStepResult(
                    step.Id,
                    step.DisplayName,
                    WorkflowExecutionState.Failed,
                    stopwatch.Elapsed,
                    exception.Message,
                    exception));

                return Finish(
                    WorkflowExecutionState.Failed,
                    context,
                    stepResults,
                    startedAt,
                    $"{step.DisplayName} failed: {exception.Message}");
            }
        }

        context.End(DateTimeOffset.UtcNow);
        progress?.Report(new WorkflowProgress(
            enabledSteps.LastOrDefault()?.Id ?? "",
            enabledSteps.LastOrDefault()?.DisplayName ?? "",
            enabledSteps.Length,
            enabledSteps.Length,
            100,
            100,
            "Workflow completed."));

        return new WorkflowResult(
            WorkflowExecutionState.Completed,
            context,
            stepResults.AsReadOnly(),
            context.EndTime!.Value - startedAt,
            "Workflow completed.");
    }

    private static void Report(
        IProgress<WorkflowProgress>? progress,
        IWorkflowStep step,
        int zeroBasedIndex,
        int stepCount,
        double stepPercentage,
        string message)
    {
        var boundedStepPercentage = Math.Clamp(stepPercentage, 0, 100);
        var overallPercentage = stepCount == 0
            ? 0
            : (zeroBasedIndex + boundedStepPercentage / 100) * 100 / stepCount;

        progress?.Report(new WorkflowProgress(
            step.Id,
            step.DisplayName,
            zeroBasedIndex + 1,
            stepCount,
            Math.Min(overallPercentage, MaximumInProgressPercentage),
            boundedStepPercentage,
            message));
    }

    private static WorkflowResult Finish(
        WorkflowExecutionState state,
        WorkflowContext context,
        List<WorkflowStepResult> stepResults,
        DateTimeOffset startedAt,
        string message)
    {
        context.End(DateTimeOffset.UtcNow);
        return new WorkflowResult(
            state,
            context,
            stepResults.AsReadOnly(),
            context.EndTime!.Value - startedAt,
            message);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

public sealed class ExtractTracksWorkflowStep(ExportService exportService)
    : IWorkflowStep
{
    public string Id => "extract-tracks";

    public string DisplayName => "Extract tracks";

    public int Order => 100;

    public bool IsEnabled => true;

    public Task<WorkflowStepResult> ExecuteAsync(
        WorkflowContext context,
        IProgress<WorkflowStepProgress> progress,
        CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                context.SetSessionDirectory(context.OutputDirectory);
                var exportProgress = new InlineExportProgress(item =>
                {
                    var percentage = item.TotalFrames == 0
                        ? 0
                        : item.FramesProcessed * 100.0 / item.TotalFrames;
                    progress.Report(new WorkflowStepProgress(
                        percentage,
                        $"Exporting session {item.SessionId} " +
                        $"({item.SessionIndex}/{item.SessionCount})."));
                });

                var files = exportService.ExportTracks(
                    context.InputDirectory,
                    "00000001.WAV",
                    new ExportRequest(context.SessionDirectory),
                    exportProgress,
                    cancellationToken);

                context.SetExtractedTrackFiles(files);
                context.SetMetadata("extractedTrackCount", files.Count.ToString());
                return WorkflowStepResult.Success(
                    $"Extracted {files.Count} track(s).");
            },
            cancellationToken);

    private sealed class InlineExportProgress(Action<ExportProgress> report)
        : IProgress<ExportProgress>
    {
        public void Report(ExportProgress value) => report(value);
    }
}
