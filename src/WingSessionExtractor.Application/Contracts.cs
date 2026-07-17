using WingSessionExtractor.Domain;

namespace WingSessionExtractor.Application;

public interface IWaveFileReader
{
    SessionSegment Read(string sessionId, string path);
}

public interface ISessionSource
{
    IReadOnlyList<SessionSegment> Scan(
        string inputDirectory,
        string fileName,
        CancellationToken cancellationToken = default);
}

public interface IChannelExporter
{
    void Export(
        IReadOnlyList<SessionSegment> segments,
        ExportRequest request,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record ExportRequest(
    string OutputDirectory,
    bool Overwrite = false,
    int? ExpectedChannels = null);

public sealed record ExportProgress(
    int SessionIndex,
    int SessionCount,
    string SessionId,
    long FramesProcessed,
    long TotalFrames);
