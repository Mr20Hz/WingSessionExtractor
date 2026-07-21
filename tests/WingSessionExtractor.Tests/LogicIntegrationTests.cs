using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WingSessionExtractor.Application;
using WingSessionExtractor.Infrastructure.Logic;

namespace WingSessionExtractor.Tests;

[TestClass]
public sealed class LogicIntegrationTests
{
    [TestMethod]
    public void Planner_OrdersChannelFilesNumerically()
    {
        var planner = new LogicProjectPlanner();

        var plan = planner.CreatePlan(Request(
            new[] { "CH10.wav", "CH02.wav", "CH01.wav" }));

        CollectionAssert.AreEqual(
            new[] { "CH01.wav", "CH02.wav", "CH10.wav" },
            plan.OrderedTrackFiles.ToArray());
        Assert.IsTrue(plan.Warnings.Single().Contains("CH03"));
    }

    [TestMethod]
    public void Planner_RejectsDuplicateChannelNumbers()
    {
        var planner = new LogicProjectPlanner();

        var exception = Assert.ThrowsException<InvalidDataException>(() =>
            planner.CreatePlan(Request(new[]
            {
                Path.Combine("one", "CH01.wav"),
                Path.Combine("two", "ch001.WAV")
            })));

        StringAssert.Contains(exception.Message, "Duplicate channel number CH01");
    }

    [TestMethod]
    public void Planner_SanitizesProjectName()
    {
        var name = LogicProjectPlanner.CreateProjectName(
            new DateOnly(2026, 7, 21),
            " Band:/\\<>|?*\" ");

        Assert.AreEqual("2026-07-21_WingSession_Band_", name);
        Assert.IsFalse(name.Any(character => "<>:\"/\\|?*".Contains(character)));
    }

    [TestMethod]
    public async Task Service_DoesNotOverwriteExistingProjectPath()
    {
        using var temporary = new TemporaryDirectory();
        var template = temporary.CreateDirectory("Template.logicx");
        var output = temporary.CreateDirectory("projects");
        var track = temporary.CreateFile("CH01.wav");
        var request = Request(new[] { track }, template, output);
        var plan = new LogicProjectPlanner().CreatePlan(request);
        Directory.CreateDirectory(plan.ProjectDirectory);
        var automation = new RecordingAutomationDriver();
        var service = CreateService(automationDriver: automation);

        var result = await service.CreateProjectAsync(request);

        Assert.AreEqual(DawProjectResultState.Conflict, result.State);
        Assert.AreEqual(plan.ProjectPath, result.ProjectPath);
        Assert.AreEqual(0, automation.CallCount);
    }

    [TestMethod]
    public async Task Service_CopiesTemplateAndPassesNumericallyOrderedTracks()
    {
        using var temporary = new TemporaryDirectory();
        var template = temporary.CreateDirectory("Template.logicx");
        var templateData = temporary.CreateFile(
            Path.Combine("Template.logicx", "Alternatives", "project-data"));
        var output = temporary.CreateDirectory("projects");
        var channel10 = temporary.CreateFile("CH10.wav");
        var channel2 = temporary.CreateFile("CH02.wav");
        var channel1 = temporary.CreateFile("CH01.wav");
        var automation = new RecordingAutomationDriver();
        var service = CreateService(automationDriver: automation);

        var result = await service.CreateProjectAsync(Request(
            new[] { channel10, channel2, channel1 },
            template,
            output));

        Assert.AreEqual(DawProjectResultState.Completed, result.State);
        Assert.IsTrue(File.Exists(templateData));
        Assert.IsTrue(File.Exists(Path.Combine(
            result.ProjectPath!,
            "Alternatives",
            "project-data")));
        CollectionAssert.AreEqual(
            new[] { channel1, channel2, channel10 },
            automation.OrderedTrackFiles!.ToArray());
    }

    [TestMethod]
    public void Service_ReportsNonMacOSCapability()
    {
        var service = CreateService(isMacOS: false);

        var capability = service.GetCapability();

        Assert.IsFalse(capability.IsAvailable);
        StringAssert.Contains(capability.Explanation, "only on macOS");
    }

    [TestMethod]
    public async Task Service_PreCancellationReturnsStructuredCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = CreateService();

        var result = await service.CreateProjectAsync(
            Request(new[] { "CH01.wav" }),
            cancellationToken: cancellation.Token);

        Assert.AreEqual(DawProjectResultState.Cancelled, result.State);
    }

    [TestMethod]
    public void LogicWorkflowStep_RunsAfterExtractionStep()
    {
        var logicStep = new CreateLogicProjectWorkflowStep(
            new StubDawProjectService(DawProjectResult.Cancelled()));

        Assert.IsTrue(logicStep.Order > 100);
        Assert.AreEqual("create-logic-project", logicStep.Id);
    }

    [TestMethod]
    public async Task WorkflowStep_StoresCreatedLogicProjectPath()
    {
        var expectedPath = Path.Combine("projects", "Session.logicx");
        var service = new StubDawProjectService(
            DawProjectResult.Completed(expectedPath));
        var step = new CreateLogicProjectWorkflowStep(service);
        var context = ConfiguredContext();
        context.SetExtractedTrackFiles(new[] { "CH01.wav" });

        var result = await step.ExecuteAsync(
            context,
            new InlineProgress<WorkflowStepProgress>(_ => { }),
            CancellationToken.None);

        Assert.AreEqual(WorkflowExecutionState.Completed, result.State);
        Assert.AreEqual(expectedPath, context.DawProjectPath);
        Assert.AreEqual(expectedPath, context.Metadata["dawProjectPath"]);
    }

    [TestMethod]
    public async Task WorkflowStepFailure_DoesNotCompleteWorkflow()
    {
        var service = new StubDawProjectService(
            DawProjectResult.Failed("Logic import failed."));
        var context = ConfiguredContext();
        context.SetExtractedTrackFiles(new[] { "CH01.wav" });
        var runner = new SequentialWorkflowRunner(new[]
        {
            new CreateLogicProjectWorkflowStep(service)
        });
        var percentages = new List<double>();

        var result = await runner.RunAsync(
            context,
            new InlineProgress<WorkflowProgress>(item =>
                percentages.Add(item.OverallPercentage)));

        Assert.AreEqual(WorkflowExecutionState.Failed, result.State);
        Assert.IsFalse(percentages.Contains(100));
    }

    [TestMethod]
    public async Task WorkflowStepCancellation_MapsToCancelled()
    {
        var service = new StubDawProjectService(
            DawProjectResult.Cancelled("Cancelled in test."));
        var context = ConfiguredContext();
        context.SetExtractedTrackFiles(new[] { "CH01.wav" });
        var runner = new SequentialWorkflowRunner(new[]
        {
            new CreateLogicProjectWorkflowStep(service)
        });

        var result = await runner.RunAsync(context);

        Assert.AreEqual(WorkflowExecutionState.Cancelled, result.State);
        Assert.AreEqual(
            WorkflowExecutionState.Cancelled,
            result.StepResults.Single().State);
    }

    [TestMethod]
    public async Task AppleScriptDriver_PassesUserPathsAsSeparateArguments()
    {
        var executor = new RecordingProcessExecutor();
        var driver = new AppleScriptLogicAutomationDriver(
            executor,
            new PollingWaiter(),
            FastTimeouts());
        var logicPath = "/Applications/Logic Pro.app";
        var projectPath = "/tmp/Client's Session/Project With Spaces.logicx";
        var trackPath = "/tmp/Client's Session/CH01.wav";

        await driver.AutomateAsync(
            logicPath,
            projectPath,
            new[] { trackPath },
            progress: null,
            CancellationToken.None);

        Assert.IsFalse(executor.Calls.Any(call => call.FileName == "/bin/sh"));
        var open = executor.Calls.Single(call => call.FileName == "/usr/bin/open");
        CollectionAssert.Contains(open.Arguments, logicPath);
        CollectionAssert.Contains(open.Arguments, projectPath);
        var chooseFile = executor.Calls.Single(call =>
            call.Arguments.Contains("choose-import-file"));
        CollectionAssert.Contains(chooseFile.Arguments, trackPath);
        Assert.IsFalse(chooseFile.Arguments.Any(argument =>
            argument.Contains("'\"'\"'", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task PollingWaiter_TimeoutNamesTheTimedOutOperation()
    {
        var waiter = new PollingWaiter();

        var exception = await Assert.ThrowsExceptionAsync<LogicAutomationException>(
            () => waiter.WaitAsync(
                "project open",
                _ => Task.FromResult(false),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None));

        Assert.AreEqual("project open", exception.Operation);
        StringAssert.Contains(exception.Message, "timed out");
    }

    [TestMethod]
    public async Task AppleScriptDriver_ExplainsMissingAccessibilityPermission()
    {
        var driver = new AppleScriptLogicAutomationDriver(
            new AccessibilityDeniedProcessExecutor(),
            new PollingWaiter(),
            FastTimeouts());

        var exception = await Assert.ThrowsExceptionAsync<LogicAutomationException>(
            () => driver.AutomateAsync(
                "/Applications/Logic Pro.app",
                "/tmp/Project.logicx",
                new[] { "/tmp/CH01.wav" },
                progress: null,
                CancellationToken.None));

        Assert.AreEqual("accessibility permission", exception.Operation);
        StringAssert.Contains(
            exception.Message,
            "System Settings → Privacy & Security → Accessibility");
    }

    [TestMethod]
    public async Task AutomationTimeout_BecomesStructuredServiceFailure()
    {
        using var temporary = new TemporaryDirectory();
        var template = temporary.CreateDirectory("Template.logicx");
        temporary.CreateFile(Path.Combine("Template.logicx", "project-data"));
        var output = temporary.CreateDirectory("projects");
        var track = temporary.CreateFile("CH01.wav");
        var timeout = new LogicAutomationException(
            "import completion",
            "Logic automation timed out while waiting for import completion.");
        var service = CreateService(
            automationDriver: new ThrowingAutomationDriver(timeout));

        var result = await service.CreateProjectAsync(
            Request(new[] { track }, template, output));

        Assert.AreEqual(DawProjectResultState.Failed, result.State);
        Assert.AreSame(timeout, result.Exception);
        Assert.AreEqual("import completion", ((LogicAutomationException)result.Exception!).Operation);
        StringAssert.Contains(result.Message, "timed out");
    }

    [TestMethod]
    public async Task Service_DryRunDoesNotCopyTemplateOrStartLogic()
    {
        using var temporary = new TemporaryDirectory();
        var template = temporary.CreateDirectory("Template.logicx");
        var output = Path.Combine(temporary.Path, "projects");
        var track = temporary.CreateFile("CH01.wav");
        var automation = new RecordingAutomationDriver();
        var service = CreateService(automationDriver: automation);
        var request = Request(new[] { track }, template, output) with
        {
            DryRun = true
        };

        var result = await service.CreateProjectAsync(request);

        Assert.AreEqual(DawProjectResultState.Completed, result.State);
        Assert.IsFalse(Directory.Exists(output));
        Assert.AreEqual(0, automation.CallCount);
        StringAssert.Contains(result.Message, "Dry run");
        StringAssert.Contains(result.Message, "CH01.wav");
    }

    private static DawProjectRequest Request(
        IReadOnlyList<string> tracks,
        string templatePath = "Template.logicx",
        string outputDirectory = "projects") =>
        new(
            templatePath,
            outputDirectory,
            "Session-01",
            new DateOnly(2026, 7, 21),
            tracks);

    private static WorkflowContext ConfiguredContext()
    {
        var context = new WorkflowContext("input/Session-01", "tracks");
        context.ConfigureDawProject(new DawProjectConfiguration(
            true,
            "Template.logicx",
            "projects",
            "Session-01",
            new DateOnly(2026, 7, 21)));
        return context;
    }

    private static LogicDawProjectService CreateService(
        bool isMacOS = true,
        ILogicAutomationDriver? automationDriver = null) =>
        new(
            new StubRuntimePlatform(isMacOS),
            new StubInstallationLocator(),
            new LogicProjectPlanner(),
            automationDriver ?? new RecordingAutomationDriver());

    private static LogicAutomationTimeouts FastTimeouts() =>
        new(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero);

    private sealed class StubRuntimePlatform(bool isMacOS) : ILogicRuntimePlatform
    {
        public bool IsMacOS => isMacOS;
    }

    private sealed class StubInstallationLocator : ILogicInstallationLocator
    {
        public string? FindLogicApplication() => "/Applications/Logic Pro.app";
    }

    private sealed class RecordingAutomationDriver : ILogicAutomationDriver
    {
        public int CallCount { get; private set; }

        public IReadOnlyList<string>? OrderedTrackFiles { get; private set; }

        public Task AutomateAsync(
            string logicApplicationPath,
            string projectPath,
            IReadOnlyList<string> orderedTrackFiles,
            IProgress<LogicAutomationProgress>? progress,
            CancellationToken cancellationToken)
        {
            CallCount++;
            OrderedTrackFiles = orderedTrackFiles;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAutomationDriver(Exception exception)
        : ILogicAutomationDriver
    {
        public Task AutomateAsync(
            string logicApplicationPath,
            string projectPath,
            IReadOnlyList<string> orderedTrackFiles,
            IProgress<LogicAutomationProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromException(exception);
    }

    private sealed class StubDawProjectService(DawProjectResult result)
        : IDawProjectService
    {
        public DawProjectCapability GetCapability() =>
            new(true, "Available for tests.");

        public Task<DawProjectResult> CreateProjectAsync(
            DawProjectRequest request,
            IProgress<DawProjectProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class RecordingProcessExecutor : IProcessExecutor
    {
        private int importDialogProbeCount;
        private int fileLocationDialogProbeCount;

        public List<ProcessCall> Calls { get; } = new();

        public Task<ProcessExecutionResult> ExecuteAsync(
            ProcessStartInfo startInfo,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var arguments = startInfo.ArgumentList.ToArray();
            Calls.Add(new ProcessCall(startInfo.FileName, arguments));
            var output = "true";
            if (arguments.Contains("import-dialog-open"))
            {
                importDialogProbeCount++;
                output = importDialogProbeCount % 2 == 1 ? "true" : "false";
            }
            else if (arguments.Contains("file-location-dialog-open"))
            {
                fileLocationDialogProbeCount++;
                output = fileLocationDialogProbeCount % 2 == 1
                    ? "true"
                    : "false";
            }

            return Task.FromResult(new ProcessExecutionResult(0, output, ""));
        }
    }

    private sealed class AccessibilityDeniedProcessExecutor : IProcessExecutor
    {
        public Task<ProcessExecutionResult> ExecuteAsync(
            ProcessStartInfo startInfo,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessExecutionResult(
                1,
                "",
                "System Events is not authorized to send Apple events."));
    }

    private sealed record ProcessCall(
        string FileName,
        string[] Arguments);

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateDirectory(string relativePath)
        {
            var path = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(path);
            return path;
        }

        public string CreateFile(string relativePath)
        {
            var path = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Array.Empty<byte>());
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
