using System.Diagnostics;

namespace WingSessionExtractor.Infrastructure.Logic;

public sealed record ProcessExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false);

public interface IProcessExecutor
{
    Task<ProcessExecutionResult> ExecuteAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed class SystemProcessExecutor : IProcessExecutor
{
    public async Task<ProcessExecutionResult> ExecuteAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Could not start process: {startInfo.FileName}");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var combinedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);

        try
        {
            await process.WaitForExitAsync(combinedCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new ProcessExecutionResult(
                -1,
                await standardOutput,
                await standardError,
                TimedOut: true);
        }

        return new ProcessExecutionResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}

public sealed class LogicAutomationException(
    string operation,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Operation { get; } = operation;
}

public sealed record LogicAutomationTimeouts(
    TimeSpan ProcessStart,
    TimeSpan ProjectOpen,
    TimeSpan ImportDialog,
    TimeSpan ImportCompletion,
    TimeSpan ProjectSave,
    TimeSpan PollInterval)
{
    public static LogicAutomationTimeouts Default { get; } = new(
        TimeSpan.FromSeconds(15),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMilliseconds(250));
}

public sealed class PollingWaiter(
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task WaitAsync(
        string operation,
        Func<CancellationToken, Task<bool>> condition,
        TimeSpan timeout,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(condition);
        var startedAt = clock.GetTimestamp();

        while (!await condition(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clock.GetElapsedTime(startedAt) >= timeout)
            {
                throw new LogicAutomationException(
                    operation,
                    $"Logic automation timed out while waiting for {operation}.");
            }

            await Task.Delay(interval, clock, cancellationToken);
        }
    }
}

public interface ILogicAutomationDriver
{
    Task AutomateAsync(
        string logicApplicationPath,
        string projectPath,
        IReadOnlyList<string> orderedTrackFiles,
        IProgress<LogicAutomationProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed record LogicAutomationProgress(
    double Percentage,
    string Operation,
    string Message);

public sealed class AppleScriptLogicAutomationDriver(
    IProcessExecutor processExecutor,
    PollingWaiter pollingWaiter,
    LogicAutomationTimeouts timeouts) : ILogicAutomationDriver
{
    private const string AccessibilityMessage =
        "Logic automation requires Accessibility permission. Open System Settings " +
        "→ Privacy & Security → Accessibility and enable WingSessionExtractor.";
    private readonly string scriptPath = Path.Combine(
        AppContext.BaseDirectory,
        "Scripts",
        "LogicAutomation.applescript");

    public async Task AutomateAsync(
        string logicApplicationPath,
        string projectPath,
        IReadOnlyList<string> orderedTrackFiles,
        IProgress<LogicAutomationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(scriptPath))
        {
            throw new LogicAutomationException(
                "automation script loading",
                $"Logic automation script was not found: {scriptPath}");
        }

        bool accessibilityAvailable;
        try
        {
            accessibilityAvailable = await ProbeAsync(
                "check-accessibility",
                cancellationToken);
        }
        catch (LogicAutomationException exception)
        {
            throw new LogicAutomationException(
                "accessibility permission",
                $"{AccessibilityMessage} Detail: {exception.Message}",
                exception);
        }

        if (!accessibilityAvailable)
        {
            throw new LogicAutomationException(
                "accessibility permission",
                AccessibilityMessage);
        }

        progress?.Report(new LogicAutomationProgress(
            5,
            "Logic process start",
            "Opening the copied project in Logic Pro."));
        await RunOpenAsync(logicApplicationPath, projectPath, cancellationToken);
        await pollingWaiter.WaitAsync(
            "Logic process start",
            token => ProbeAsync("process-running", token),
            timeouts.ProcessStart,
            timeouts.PollInterval,
            cancellationToken);

        progress?.Report(new LogicAutomationProgress(
            15,
            "project open",
            "Waiting for the Logic project to open."));
        await pollingWaiter.WaitAsync(
            "project open",
            token => ProbeAsync("project-open", token),
            timeouts.ProjectOpen,
            timeouts.PollInterval,
            cancellationToken);

        for (var index = 0; index < orderedTrackFiles.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var track = orderedTrackFiles[index];
            var percentage = 20 + (index * 70.0 / orderedTrackFiles.Count);
            progress?.Report(new LogicAutomationProgress(
                percentage,
                "track import",
                $"Importing {Path.GetFileName(track)}."));

            await RunScriptAsync("open-import-dialog", cancellationToken);
            await pollingWaiter.WaitAsync(
                "import dialog availability",
                token => ProbeAsync("import-dialog-open", token),
                timeouts.ImportDialog,
                timeouts.PollInterval,
                cancellationToken);
            await RunScriptAsync("show-file-location-dialog", cancellationToken);
            await pollingWaiter.WaitAsync(
                "file location dialog availability",
                token => ProbeAsync("file-location-dialog-open", token),
                timeouts.ImportDialog,
                timeouts.PollInterval,
                cancellationToken);
            await RunScriptAsync("choose-import-file", cancellationToken, track);
            await pollingWaiter.WaitAsync(
                $"file selection for {Path.GetFileName(track)}",
                async token => !await ProbeAsync(
                    "file-location-dialog-open",
                    token),
                timeouts.ImportDialog,
                timeouts.PollInterval,
                cancellationToken);
            await RunScriptAsync("confirm-import-file", cancellationToken);
            await pollingWaiter.WaitAsync(
                $"import completion for {Path.GetFileName(track)}",
                async token => !await ProbeAsync("import-dialog-open", token),
                timeouts.ImportCompletion,
                timeouts.PollInterval,
                cancellationToken);
        }

        progress?.Report(new LogicAutomationProgress(
            95,
            "project save",
            "Saving the Logic project."));
        await RunScriptAsync("save-project", cancellationToken);
        await pollingWaiter.WaitAsync(
            "project save",
            token => ProbeAsync("project-saved", token),
            timeouts.ProjectSave,
            timeouts.PollInterval,
            cancellationToken);
        progress?.Report(new LogicAutomationProgress(
            100,
            "complete",
            "Logic project created and saved."));
    }

    private async Task RunOpenAsync(
        string logicApplicationPath,
        string projectPath,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo("/usr/bin/open");
        startInfo.ArgumentList.Add("-a");
        startInfo.ArgumentList.Add(logicApplicationPath);
        startInfo.ArgumentList.Add(projectPath);
        var result = await processExecutor.ExecuteAsync(
            startInfo,
            timeouts.ProcessStart,
            cancellationToken);
        EnsureSuccess("Logic process start", result);
    }

    private async Task<bool> ProbeAsync(
        string operation,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteScriptAsync(operation, cancellationToken);
        EnsureSuccess(operation, result);
        return bool.TryParse(result.StandardOutput.Trim(), out var value) && value;
    }

    private async Task RunScriptAsync(
        string operation,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var result = await ExecuteScriptAsync(
            operation,
            cancellationToken,
            arguments);
        EnsureSuccess(operation, result);
    }

    private Task<ProcessExecutionResult> ExecuteScriptAsync(
        string operation,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var startInfo = CreateStartInfo("/usr/bin/osascript");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add(operation);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return processExecutor.ExecuteAsync(
            startInfo,
            timeouts.ProcessStart,
            cancellationToken);
    }

    private static ProcessStartInfo CreateStartInfo(string fileName) =>
        new(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

    private static void EnsureSuccess(
        string operation,
        ProcessExecutionResult result)
    {
        if (result.TimedOut)
        {
            throw new LogicAutomationException(
                operation,
                $"Logic automation timed out during {operation}.");
        }

        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? "The automation command failed."
                : result.StandardError.Trim();
            throw new LogicAutomationException(
                operation,
                $"Logic automation failed during {operation}: {detail}");
        }
    }
}
