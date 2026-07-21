namespace WingSessionExtractor.Infrastructure.Logic;

public interface ILogicRuntimePlatform
{
    bool IsMacOS { get; }
}

public sealed class LogicRuntimePlatform : ILogicRuntimePlatform
{
    public bool IsMacOS => OperatingSystem.IsMacOS();
}

public interface ILogicInstallationLocator
{
    string? FindLogicApplication();
}

public sealed class LogicInstallationLocator : ILogicInstallationLocator
{
    private static readonly string[] CandidatePaths =
    {
        "/Applications/Logic Pro.app",
        "/System/Applications/Logic Pro.app"
    };

    public string? FindLogicApplication() =>
        CandidatePaths.FirstOrDefault(Directory.Exists);
}
