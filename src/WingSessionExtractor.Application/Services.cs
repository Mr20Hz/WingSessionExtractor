using WingSessionExtractor.Domain;

namespace WingSessionExtractor.Application;

public sealed class InspectService(ISessionSource source)
{
    public InspectionReport Inspect(string input, string fileName = "00000001.WAV")
    {
        var segments = source.Scan(input, fileName);
        if (segments.Count == 0)
        {
            throw new InvalidOperationException("No WING session files found.");
        }

        var format = segments[0].Format;
        foreach (var segment in segments.Skip(1))
        {
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
        CancellationToken cancellationToken = default)
    {
        var segments = source.Scan(input, fileName);
        if (segments.Count == 0)
        {
            throw new InvalidOperationException("No WING session files found.");
        }

        exporter.Export(segments, request, progress, cancellationToken);
    }
}
