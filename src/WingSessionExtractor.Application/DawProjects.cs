namespace WingSessionExtractor.Application;

public enum DawProjectResultState
{
    Completed,
    Conflict,
    Failed,
    Cancelled
}

public sealed record DawProjectCapability(
    bool IsAvailable,
    string Explanation);

public sealed record DawProjectConfiguration(
    bool IsEnabled,
    string TemplatePath,
    string ProjectOutputDirectory,
    string? SessionIdentifier = null,
    DateOnly? SessionDate = null,
    int? ExpectedChannelCount = null);

public sealed record DawProjectRequest(
    string TemplatePath,
    string ProjectOutputDirectory,
    string SessionIdentifier,
    DateOnly SessionDate,
    IReadOnlyList<string> TrackFiles,
    int? ExpectedChannelCount = null,
    bool DryRun = false);

public sealed record DawProjectProgress(
    double Percentage,
    string Operation,
    string Message);

public sealed record DawProjectResult(
    DawProjectResultState State,
    string? ProjectPath,
    IReadOnlyList<string> Warnings,
    string Message,
    Exception? Exception = null)
{
    public static DawProjectResult Completed(
        string projectPath,
        IReadOnlyList<string>? warnings = null,
        string message = "DAW project created.") =>
        new(
            DawProjectResultState.Completed,
            projectPath,
            warnings ?? Array.Empty<string>(),
            message);

    public static DawProjectResult Conflict(
        string projectPath,
        string message) =>
        new(
            DawProjectResultState.Conflict,
            projectPath,
            Array.Empty<string>(),
            message);

    public static DawProjectResult Failed(
        string message,
        Exception? exception = null,
        IReadOnlyList<string>? warnings = null) =>
        new(
            DawProjectResultState.Failed,
            null,
            warnings ?? Array.Empty<string>(),
            message,
            exception);

    public static DawProjectResult Cancelled(string message = "Cancelled.") =>
        new(
            DawProjectResultState.Cancelled,
            null,
            Array.Empty<string>(),
            message);
}

public interface IDawProjectService
{
    DawProjectCapability GetCapability();

    Task<DawProjectResult> CreateProjectAsync(
        DawProjectRequest request,
        IProgress<DawProjectProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
