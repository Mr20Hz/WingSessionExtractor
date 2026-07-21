using WingSessionExtractor.Application;

namespace WingSessionExtractor.Infrastructure.Logic;

public sealed class LogicDawProjectService(
    ILogicRuntimePlatform runtimePlatform,
    ILogicInstallationLocator installationLocator,
    LogicProjectPlanner projectPlanner,
    ILogicAutomationDriver automationDriver) : IDawProjectService
{
    public DawProjectCapability GetCapability()
    {
        if (!runtimePlatform.IsMacOS)
        {
            return new DawProjectCapability(
                false,
                "Logic project creation is available only on macOS.");
        }

        if (installationLocator.FindLogicApplication() is null)
        {
            return new DawProjectCapability(
                false,
                "Logic Pro was not found in /Applications or /System/Applications.");
        }

        return new DawProjectCapability(
            true,
            "Logic Pro project creation is available.");
    }

    public async Task<DawProjectResult> CreateProjectAsync(
        DawProjectRequest request,
        IProgress<DawProjectProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var capability = GetCapability();
        if (!capability.IsAvailable)
        {
            return DawProjectResult.Failed(capability.Explanation);
        }

        LogicProjectPlan? plan = null;
        var automationStarted = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateTemplate(request.TemplatePath);
            plan = projectPlanner.CreatePlan(request);
            ValidateTracks(plan.OrderedTrackFiles);
            if (Directory.Exists(plan.ProjectDirectory) ||
                File.Exists(plan.ProjectDirectory) ||
                Directory.Exists(plan.ProjectPath) ||
                File.Exists(plan.ProjectPath))
            {
                return DawProjectResult.Conflict(
                    plan.ProjectPath,
                    $"Logic project already exists: {plan.ProjectPath}");
            }

            progress?.Report(new DawProjectProgress(
                0,
                "validation",
                $"Validated {plan.OrderedTrackFiles.Count} channel track(s)."));

            if (request.DryRun)
            {
                return DawProjectResult.Completed(
                    plan.ProjectPath,
                    plan.Warnings,
                    $"Dry run completed. Intended Logic project: {plan.ProjectPath}. " +
                    $"Ordered tracks: {string.Join(", ", plan.OrderedTrackFiles.Select(Path.GetFileName))}");
            }

            Directory.CreateDirectory(request.ProjectOutputDirectory);
            CopyTemplate(
                request.TemplatePath,
                plan.ProjectDirectory,
                plan.ProjectPath,
                cancellationToken);
            progress?.Report(new DawProjectProgress(
                5,
                "template copy",
                $"Copied the Logic template to {plan.ProjectPath}."));

            var automationProgress = new InlineProgress<LogicAutomationProgress>(item =>
                progress?.Report(new DawProjectProgress(
                    5 + item.Percentage * 0.95,
                    item.Operation,
                    item.Message)));
            automationStarted = true;
            await automationDriver.AutomateAsync(
                installationLocator.FindLogicApplication()!,
                plan.ProjectPath,
                plan.OrderedTrackFiles,
                automationProgress,
                cancellationToken);

            return DawProjectResult.Completed(
                plan.ProjectPath,
                plan.Warnings,
                $"Logic project created and saved: {plan.ProjectPath}");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            if (!automationStarted && plan is not null)
            {
                TryDeleteIncompleteCopy(plan.ProjectDirectory);
            }

            return DawProjectResult.Cancelled(
                "Logic project creation was cancelled.");
        }
        catch (Exception exception)
        {
            if (!automationStarted && plan is not null)
            {
                TryDeleteIncompleteCopy(plan.ProjectDirectory);
            }

            var retainedProject = automationStarted && plan is not null
                ? $" The copied project remains at {plan.ProjectPath}."
                : "";
            return DawProjectResult.Failed(
                exception.Message + retainedProject,
                exception);
        }
    }

    private static void ValidateTemplate(string templatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        if (!Directory.Exists(templatePath) && !File.Exists(templatePath))
        {
            throw new FileNotFoundException(
                "The selected Logic template does not exist.",
                templatePath);
        }
    }

    private static void ValidateTracks(IEnumerable<string> trackFiles)
    {
        var missingTrack = trackFiles.FirstOrDefault(path => !File.Exists(path));
        if (missingTrack is not null)
        {
            throw new FileNotFoundException(
                "An extracted track file does not exist.",
                missingTrack);
        }
    }

    private static void CopyTemplate(
        string templatePath,
        string projectDirectory,
        string projectPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(projectDirectory);
        if (File.Exists(templatePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(templatePath, projectPath, overwrite: false);
            return;
        }

        Directory.CreateDirectory(projectPath);
        foreach (var directory in Directory.EnumerateDirectories(
                     templatePath,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(templatePath, directory);
            Directory.CreateDirectory(Path.Combine(projectPath, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(
                     templatePath,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(templatePath, file);
            var destination = Path.Combine(projectPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: false);
        }
    }

    private static void TryDeleteIncompleteCopy(string projectDirectory)
    {
        try
        {
            if (Directory.Exists(projectDirectory))
            {
                Directory.Delete(projectDirectory, recursive: true);
            }
            else if (File.Exists(projectDirectory))
            {
                File.Delete(projectDirectory);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

public sealed class CreateLogicProjectWorkflowStep(
    IDawProjectService dawProjectService) : IWorkflowStep
{
    public string Id => "create-logic-project";

    public string DisplayName => "Create Logic Pro project";

    public int Order => 200;

    public bool IsEnabled => true;

    public async Task<WorkflowStepResult> ExecuteAsync(
        WorkflowContext context,
        IProgress<WorkflowStepProgress> progress,
        CancellationToken cancellationToken)
    {
        var configuration = context.DawProjectConfiguration;
        if (configuration is null || !configuration.IsEnabled)
        {
            return WorkflowStepResult.Success(
                "Logic project creation is disabled; step skipped.");
        }

        var capability = dawProjectService.GetCapability();
        if (!capability.IsAvailable)
        {
            return WorkflowStepResult.Success(
                $"Logic project creation skipped: {capability.Explanation}");
        }

        var sessionIdentifier = string.IsNullOrWhiteSpace(
            configuration.SessionIdentifier)
            ? GetSessionIdentifier(context.InputDirectory)
            : configuration.SessionIdentifier;
        var sessionDate = configuration.SessionDate ?? GetSessionDate(context);
        var request = new DawProjectRequest(
            configuration.TemplatePath,
            configuration.ProjectOutputDirectory,
            sessionIdentifier,
            sessionDate,
            context.ExtractedTrackFiles,
            configuration.ExpectedChannelCount);
        var dawProgress = new InlineProgress<DawProjectProgress>(item =>
            progress.Report(new WorkflowStepProgress(
                item.Percentage,
                item.Message)));
        var result = await dawProjectService.CreateProjectAsync(
            request,
            dawProgress,
            cancellationToken);

        if (result.State == DawProjectResultState.Cancelled)
        {
            return WorkflowStepResult.Cancelled(result.Message);
        }

        if (result.State != DawProjectResultState.Completed ||
            string.IsNullOrWhiteSpace(result.ProjectPath))
        {
            return WorkflowStepResult.Failure(
                result.Message,
                result.Exception);
        }

        context.SetDawProjectPath(result.ProjectPath);
        context.SetMetadata("dawProjectPath", result.ProjectPath);
        var warningText = result.Warnings.Count == 0
            ? ""
            : $" Warnings: {string.Join(" ", result.Warnings)}";
        return WorkflowStepResult.Success(result.Message + warningText);
    }

    private static string GetSessionIdentifier(string inputDirectory)
    {
        var normalized = Path.TrimEndingDirectorySeparator(inputDirectory);
        var name = Path.GetFileName(normalized);
        return string.IsNullOrWhiteSpace(name) ? "Session" : name;
    }

    private static DateOnly GetSessionDate(WorkflowContext context)
    {
        if (Directory.Exists(context.InputDirectory))
        {
            return DateOnly.FromDateTime(
                Directory.GetLastWriteTime(context.InputDirectory));
        }

        return DateOnly.FromDateTime(
            (context.StartTime ?? DateTimeOffset.Now).LocalDateTime);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
