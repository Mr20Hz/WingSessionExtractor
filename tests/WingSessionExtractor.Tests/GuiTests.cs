using Microsoft.VisualStudio.TestTools.UnitTesting;
using WingSessionExtractor.Application;
using WingSessionExtractor.Gui;

namespace WingSessionExtractor.Tests;

[TestClass]
public sealed class GuiTests
{
    [TestMethod]
    public void SettingsStore_RoundTripsDirectories()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");

        try
        {
            var store = new JsonDirectorySettingsStore(path);
            var expected = new DirectorySettings(
                "input",
                "output",
                EnableLogicProjectCreation: true,
                LogicTemplatePath: "template.logicx",
                LogicProjectOutputDirectory: "logic-projects");

            store.Save(expected);

            Assert.AreEqual(expected, store.Load());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task ViewModel_UsesSelectedDirectoriesAndShowsCompletion()
    {
        var runner = new RecordingWorkflowRunner();
        var settings = new MemorySettingsStore(
            new DirectorySettings("remembered-input", "remembered-output"));
        var viewModel = CreateViewModel(runner, settings);

        await viewModel.StartWorkflowAsync();

        Assert.AreEqual("remembered-input", runner.Context?.InputDirectory);
        Assert.AreEqual("remembered-output", runner.Context?.OutputDirectory);
        Assert.AreEqual("Workflow completed.", viewModel.StatusText);
        Assert.AreEqual(100d, viewModel.OverallProgress);
        Assert.AreEqual(WorkflowExecutionState.Completed, viewModel.ExecutionState);
        Assert.IsFalse(viewModel.IsRunning);
        Assert.IsTrue(viewModel.CanStart);
        Assert.IsFalse(viewModel.CanCancel);
        Assert.IsTrue(viewModel.CanEdit);
        Assert.IsTrue(viewModel.HasFinalSummary);
        Assert.AreEqual(
            new DirectorySettings("remembered-input", "remembered-output"),
            settings.Settings);
    }

    [TestMethod]
    public async Task ViewModel_ChangesButtonStatesWhileRunningAndCancelling()
    {
        var runner = new ControlledWorkflowRunner();
        var viewModel = CreateViewModel(runner);

        Assert.IsTrue(viewModel.CanStart);
        Assert.IsFalse(viewModel.CanCancel);
        Assert.IsTrue(viewModel.CanEdit);

        var execution = viewModel.StartWorkflowAsync();

        Assert.IsFalse(viewModel.CanStart);
        Assert.IsTrue(viewModel.CanCancel);
        Assert.IsFalse(viewModel.CanEdit);
        Assert.IsFalse(viewModel.StartCommand.CanExecute(null));
        Assert.IsTrue(viewModel.CancelCommand.CanExecute(null));

        viewModel.CancelWorkflow();

        Assert.AreEqual(WorkflowExecutionState.Cancelling, viewModel.ExecutionState);
        Assert.AreEqual("Cancelling…", viewModel.StatusText);
        Assert.IsFalse(viewModel.CanStart);
        Assert.IsFalse(viewModel.CanCancel);
        Assert.IsFalse(viewModel.StartCommand.CanExecute(null));
        Assert.IsFalse(viewModel.CancelCommand.CanExecute(null));

        await execution;

        Assert.AreEqual(WorkflowExecutionState.Cancelled, viewModel.ExecutionState);
        Assert.AreEqual("Workflow cancelled.", viewModel.StatusText);
        Assert.IsTrue(viewModel.CanStart);
        Assert.IsFalse(viewModel.CanCancel);
        Assert.IsTrue(viewModel.CanEdit);
        Assert.IsFalse(viewModel.HasError);
    }

    [TestMethod]
    public async Task ViewModel_PreventsConcurrentRuns()
    {
        var runner = new ControlledWorkflowRunner();
        var viewModel = CreateViewModel(runner);

        var first = viewModel.StartWorkflowAsync();
        var second = viewModel.StartWorkflowAsync();

        Assert.AreEqual(1, runner.RunCount);
        Assert.IsTrue(second.IsCompletedSuccessfully);

        viewModel.CancelWorkflow();
        await first;
    }

    [TestMethod]
    public async Task ViewModel_DisplaysStructuredWorkflowFailure()
    {
        var viewModel = CreateViewModel(new FailingWorkflowRunner());

        await viewModel.StartWorkflowAsync();

        Assert.AreEqual(WorkflowExecutionState.Failed, viewModel.ExecutionState);
        Assert.AreEqual("Workflow failed.", viewModel.StatusText);
        Assert.AreEqual("Test failure", viewModel.ErrorMessage);
        Assert.IsTrue(viewModel.HasError);
        Assert.IsTrue(viewModel.HasFinalSummary);
        Assert.IsTrue(viewModel.CanStart);
    }

    [TestMethod]
    public void ViewModel_DisablesLogicSettingsWhenCapabilityIsUnavailable()
    {
        var settings = new MemorySettingsStore(new DirectorySettings(
            "input",
            "output",
            EnableLogicProjectCreation: true,
            LogicTemplatePath: "template.logicx",
            LogicProjectOutputDirectory: "projects"));
        var viewModel = CreateViewModel(
            new RecordingWorkflowRunner(),
            settings,
            new UnavailableDawProjectService());

        Assert.IsFalse(viewModel.EnableLogicProjectCreation);
        Assert.IsFalse(viewModel.CanToggleLogicProjectCreation);
        Assert.IsFalse(viewModel.CanEditLogicSettings);
        Assert.IsTrue(viewModel.CanStart);
        StringAssert.Contains(
            viewModel.LogicCapability.Explanation,
            "only on macOS");
    }

    private static MainWindowViewModel CreateViewModel(
        IWorkflowRunner runner,
        MemorySettingsStore? settings = null,
        IDawProjectService? dawProjectService = null) =>
        new(
            runner,
            new NullFolderPicker(),
            new NullFilePicker(),
            dawProjectService ?? new AvailableDawProjectService(),
            settings ?? new MemorySettingsStore(
                new DirectorySettings("input", "output")));

    private sealed class RecordingWorkflowRunner : IWorkflowRunner
    {
        public IReadOnlyList<WorkflowStepDescriptor> Steps { get; } =
            new[] { new WorkflowStepDescriptor("extract", "Extract", 10, true) };

        public WorkflowContext? Context { get; private set; }

        public Task<WorkflowResult> RunAsync(
            WorkflowContext context,
            IProgress<WorkflowProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Context = context;
            return Task.FromResult(Completed(context));
        }
    }

    private sealed class ControlledWorkflowRunner : IWorkflowRunner
    {
        public IReadOnlyList<WorkflowStepDescriptor> Steps { get; } =
            new[] { new WorkflowStepDescriptor("wait", "Wait", 10, true) };

        public int RunCount { get; private set; }

        public async Task<WorkflowResult> RunAsync(
            WorkflowContext context,
            IProgress<WorkflowProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            RunCount++;
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Completed(context);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return new WorkflowResult(
                    WorkflowExecutionState.Cancelled,
                    context,
                    Array.Empty<WorkflowStepResult>(),
                    TimeSpan.Zero,
                    "Workflow cancelled.");
            }
        }
    }

    private sealed class FailingWorkflowRunner : IWorkflowRunner
    {
        public IReadOnlyList<WorkflowStepDescriptor> Steps { get; } =
            new[] { new WorkflowStepDescriptor("fail", "Fail", 10, true) };

        public Task<WorkflowResult> RunAsync(
            WorkflowContext context,
            IProgress<WorkflowProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkflowResult(
                WorkflowExecutionState.Failed,
                context,
                new[]
                {
                    new WorkflowStepResult(
                        "fail",
                        "Fail",
                        WorkflowExecutionState.Failed,
                        TimeSpan.Zero,
                        "Test failure")
                },
                TimeSpan.Zero,
                "Workflow failed."));
    }

    private sealed class NullFolderPicker : IFolderPicker
    {
        public Task<string?> PickAsync(
            string title,
            string currentDirectory) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NullFilePicker : IFilePicker
    {
        public Task<string?> PickAsync(
            string title,
            string currentPath,
            IReadOnlyList<string> patterns) =>
            Task.FromResult<string?>(null);
    }

    private sealed class AvailableDawProjectService : IDawProjectService
    {
        public DawProjectCapability GetCapability() =>
            new(true, "Available for tests.");

        public Task<DawProjectResult> CreateProjectAsync(
            DawProjectRequest request,
            IProgress<DawProjectProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnavailableDawProjectService : IDawProjectService
    {
        public DawProjectCapability GetCapability() =>
            new(false, "Logic project creation is available only on macOS.");

        public Task<DawProjectResult> CreateProjectAsync(
            DawProjectRequest request,
            IProgress<DawProjectProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MemorySettingsStore(DirectorySettings settings)
        : IDirectorySettingsStore
    {
        public DirectorySettings Settings { get; private set; } = settings;

        public DirectorySettings Load() => Settings;

        public void Save(DirectorySettings value) => Settings = value;
    }

    private static WorkflowResult Completed(WorkflowContext context) =>
        new(
            WorkflowExecutionState.Completed,
            context,
            Array.Empty<WorkflowStepResult>(),
            TimeSpan.Zero,
            "Workflow completed.");
}
