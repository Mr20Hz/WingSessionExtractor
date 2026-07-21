using WingSessionExtractor.Domain;

namespace WingSessionExtractor.Application;

public sealed class InspectService(ISessionSource source)
{
    public InspectionReport Inspect(
        string input,
        string fileName = "00000001.WAV",
        CancellationToken cancellationToken = default)
    {
        var segments = source.Scan(input, fileName, cancellationToken);
        if (segments.Count == 0)
        {
            throw new InvalidOperationException("No WING session files found.");
        }

        var format = segments[0].Format;
        foreach (var segment in segments.Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCompatible(format, segment.Format, segment.FilePath);
        }

        return new InspectionReport(
            segments,
            format,
            segments.Sum(item => item.FrameCount));
    }

    public static void EnsureCompatible(
        WaveFormat expected,
        WaveFormat actual,
        string filePath)
    {
        if (expected != actual)
        {
            throw new InvalidDataException(
                $"Incompatible WAV format: {filePath}");
        }
    }
}

public sealed class ExportService(
    ISessionSource source,
    IChannelExporter exporter)
{
    public void Export(
        string input,
        string fileName,
        ExportRequest request,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        ExportTracks(input, fileName, request, progress, cancellationToken);

    public IReadOnlyList<string> ExportTracks(
        string input,
        string fileName,
        ExportRequest request,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var segments = source.Scan(input, fileName, cancellationToken);
        if (segments.Count == 0)
        {
            throw new InvalidOperationException("No WING session files found.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        exporter.Export(segments, request, progress, cancellationToken);

        return Enumerable.Range(1, segments[0].Format.Channels)
            .Select(channel => Path.Combine(
                request.OutputDirectory,
                $"CH{channel:00}.wav"))
            .ToArray();
    }
}
