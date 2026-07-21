using Microsoft.VisualStudio.TestTools.UnitTesting;
using WingSessionExtractor.Application;
using WingSessionExtractor.Domain;

namespace WingSessionExtractor.Tests;

[TestClass]
public sealed class WorkflowTests
{
    [TestMethod]
    public async Task Runner_ExecutesEnabledStepsInDeterministicOrder()
    {
        var calls = new List<string>();
        var runner = new SequentialWorkflowRunner(new IWorkflowStep[]
        {
            new TestStep("third", 30, (_, _, _) =>
            {
                calls.Add("third");
                return Task.FromResult(WorkflowStepResult.Success());
            }),
            new TestStep("first", 10, (_, _, _) =>
            {
                calls.Add("first");
                return Task.FromResult(WorkflowStepResult.Success());
            }),
            new TestStep("second", 20, (_, _, _) =>
            {
                calls.Add("second");
                return Task.FromResult(WorkflowStepResult.Success());
            }),
            new TestStep("disabled", 0, (_, _, _) =>
            {
                calls.Add("disabled");
                return Task.FromResult(WorkflowStepResult.Success());
            }, isEnabled: false)
        });

        var result = await runner.RunAsync(new WorkflowContext("in", "out"));

        CollectionAssert.AreEqual(
            new[] { "first", "second", "third" },
            calls);
        Assert.AreEqual(WorkflowExecutionState.Completed, result.State);
        Assert.AreEqual(3, result.StepResults.Count);
        Assert.IsTrue(result.StepResults.All(step => step.Duration >= TimeSpan.Zero));
    }

    [TestMethod]
    public async Task Runner_StopsAfterFirstFailedStep()
    {
        var laterStepExecuted = false;
        var runner = new SequentialWorkflowRunner(new IWorkflowStep[]
        {
            new TestStep(
                "failure",
                10,
                (_, _, _) => Task.FromResult(
                    WorkflowStepResult.Failure("Expected failure."))),
            new TestStep("later", 20, (_, _, _) =>
            {
                laterStepExecuted = true;
                return Task.FromResult(WorkflowStepResult.Success());
            })
        });

        var result = await runner.RunAsync(new WorkflowContext("in", "out"));

        Assert.AreEqual(WorkflowExecutionState.Failed, result.State);
        Assert.AreEqual(1, result.StepResults.Count);
        Assert.IsFalse(laterStepExecuted);
    }

    [TestMethod]
    public async Task Runner_CancellationStopsWorkflowAndReturnsCancelled()
    {
        var laterStepExecuted = false;
        var runner = new SequentialWorkflowRunner(new IWorkflowStep[]
        {
            new TestStep("wait", 10, async (_, _, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return WorkflowStepResult.Success();
            }),
            new TestStep("later", 20, (_, _, _) =>
            {
                laterStepExecuted = true;
                return Task.FromResult(WorkflowStepResult.Success());
            })
        });
        using var cancellation = new CancellationTokenSource();

        var execution = runner.RunAsync(
            new WorkflowContext("in", "out"),
            cancellationToken: cancellation.Token);
        cancellation.Cancel();
        var result = await execution;

        Assert.AreEqual(WorkflowExecutionState.Cancelled, result.State);
        Assert.AreEqual(WorkflowExecutionState.Cancelled, result.StepResults[0].State);
        Assert.IsFalse(laterStepExecuted);
    }

    [TestMethod]
    public async Task Runner_ReportsOneHundredOnlyAfterSuccessfulCompletion()
    {
        var releaseStep = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var progressValues = new List<double>();
        var runner = new SequentialWorkflowRunner(new[]
        {
            new TestStep("step", 10, async (_, progress, _) =>
            {
                progress.Report(new WorkflowStepProgress(100, "Work written."));
                await releaseStep.Task;
                return WorkflowStepResult.Success();
            })
        });

        var execution = runner.RunAsync(
            new WorkflowContext("in", "out"),
            new InlineProgress<WorkflowProgress>(item =>
                progressValues.Add(item.OverallPercentage)));

        Assert.IsFalse(execution.IsCompleted);
        Assert.IsFalse(progressValues.Contains(100));

        releaseStep.SetResult();
        var result = await execution;

        Assert.AreEqual(WorkflowExecutionState.Completed, result.State);
        Assert.AreEqual(100, progressValues[^1]);
        Assert.AreEqual(1, progressValues.Count(value => value == 100));
    }

    [TestMethod]
    public async Task Runner_ConvertsUnexpectedExceptionToStructuredFailure()
    {
        var expected = new InvalidOperationException("Boom");
        var runner = new SequentialWorkflowRunner(new[]
        {
            new TestStep(
                "throwing",
                10,
                (_, _, _) => Task.FromException<WorkflowStepResult>(expected))
        });

        var result = await runner.RunAsync(new WorkflowContext("in", "out"));

        Assert.AreEqual(WorkflowExecutionState.Failed, result.State);
        Assert.AreEqual(1, result.StepResults.Count);
        Assert.AreSame(expected, result.StepResults[0].Exception);
        Assert.AreEqual("Boom", result.StepResults[0].Message);
    }

    [TestMethod]
    public async Task ExtractStep_UsesExportServiceAndUpdatesContext()
    {
        var format = new WaveFormat(1, 2, 48000, 8, 32);
        var source = new StubSessionSource(new SessionSegment(
            "0001",
            "source.wav",
            format,
            0,
            800));
        var exporter = new ReportingExporter();
        var step = new ExtractTracksWorkflowStep(
            new ExportService(source, exporter));
        var context = new WorkflowContext("input", "output");
        var percentages = new List<double>();

        var result = await step.ExecuteAsync(
            context,
            new InlineProgress<WorkflowStepProgress>(item =>
                percentages.Add(item.Percentage)),
            CancellationToken.None);

        Assert.AreEqual(WorkflowExecutionState.Completed, result.State);
        Assert.AreEqual("output", exporter.Request?.OutputDirectory);
        CollectionAssert.AreEqual(
            new[]
            {
                Path.Combine("output", "CH01.wav"),
                Path.Combine("output", "CH02.wav")
            },
            context.ExtractedTrackFiles.ToArray());
        CollectionAssert.AreEqual(new[] { 100d }, percentages);
        Assert.AreEqual("2", context.Metadata["extractedTrackCount"]);
    }

    private sealed class TestStep(
        string id,
        int order,
        Func<WorkflowContext, IProgress<WorkflowStepProgress>, CancellationToken,
            Task<WorkflowStepResult>> execute,
        bool isEnabled = true) : IWorkflowStep
    {
        public string Id => id;

        public string DisplayName => id;

        public int Order => order;

        public bool IsEnabled => isEnabled;

        public Task<WorkflowStepResult> ExecuteAsync(
            WorkflowContext context,
            IProgress<WorkflowStepProgress> progress,
            CancellationToken cancellationToken) =>
            execute(context, progress, cancellationToken);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class StubSessionSource(SessionSegment segment) : ISessionSource
    {
        public IReadOnlyList<SessionSegment> Scan(
            string inputDirectory,
            string fileName,
            CancellationToken cancellationToken = default) =>
            new[] { segment };
    }

    private sealed class ReportingExporter : IChannelExporter
    {
        public ExportRequest? Request { get; private set; }

        public void Export(
            IReadOnlyList<SessionSegment> segments,
            ExportRequest request,
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            progress?.Report(new ExportProgress(
                1,
                1,
                segments[0].SessionId,
                segments[0].FrameCount,
                segments[0].FrameCount));
        }
    }
}
