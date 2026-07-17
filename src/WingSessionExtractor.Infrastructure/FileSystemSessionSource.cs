using System.Globalization;
using System.Runtime.CompilerServices;
using WingSessionExtractor.Application;
using WingSessionExtractor.Domain;

[assembly: InternalsVisibleTo("WingSessionExtractor.Tests")]

namespace WingSessionExtractor.Infrastructure;

public sealed class FileSystemSessionSource(IWaveFileReader reader) : ISessionSource
{
    public IReadOnlyList<SessionSegment> Scan(
        string inputDirectory,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(inputDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(root);
        }

        var segments = new List<SessionSegment>();
        foreach (var path in Directory.EnumerateFiles(
                     root,
                     fileName,
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileName(Path.GetDirectoryName(path)!)!;
            segments.Add(reader.Read(id, path));
        }

        cancellationToken.ThrowIfCancellationRequested();

        return segments
            .OrderBy(item => ParseHex(item.SessionId))
            .ThenBy(item => item.SessionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static ulong ParseHex(string value) =>
        ulong.TryParse(
            value,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out var result)
            ? result
            : ulong.MaxValue;
}
