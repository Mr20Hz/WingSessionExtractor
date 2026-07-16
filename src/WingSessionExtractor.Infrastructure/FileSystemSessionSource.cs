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
        string fileName)
    {
        var root = Path.GetFullPath(inputDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(root);
        }

        return Directory
            .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .Select(path =>
            {
                var id = Path.GetFileName(Path.GetDirectoryName(path)!)!;
                return reader.Read(id, path);
            })
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
