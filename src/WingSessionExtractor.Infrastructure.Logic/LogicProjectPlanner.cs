using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WingSessionExtractor.Application;

namespace WingSessionExtractor.Infrastructure.Logic;

public sealed record LogicProjectPlan(
    string ProjectName,
    string ProjectDirectory,
    string ProjectPath,
    IReadOnlyList<string> OrderedTrackFiles,
    IReadOnlyList<string> Warnings);

public sealed partial class LogicProjectPlanner
{
    public LogicProjectPlan CreatePlan(DawProjectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectOutputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionIdentifier);

        var channels = request.TrackFiles
            .Select(path => new
            {
                Path = path,
                Channel = ParseChannelNumber(path)
            })
            .ToArray();

        var duplicate = channels
            .GroupBy(item => item.Channel)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException(
                $"Duplicate channel number CH{duplicate.Key:00}.");
        }

        if (channels.Length == 0)
        {
            throw new InvalidDataException("No extracted channel files supplied.");
        }

        var channelNumbers = channels
            .Select(item => item.Channel)
            .ToHashSet();
        var upperChannel = request.ExpectedChannelCount ?? channels.Max(item => item.Channel);
        var missing = Enumerable.Range(1, upperChannel)
            .Where(channel => !channelNumbers.Contains(channel))
            .ToArray();

        if (request.ExpectedChannelCount is not null && missing.Length > 0)
        {
            throw new InvalidDataException(
                $"Required channel files are missing: {FormatChannels(missing)}.");
        }

        var warnings = missing.Length == 0
            ? Array.Empty<string>()
            : new[] { $"Missing channel files: {FormatChannels(missing)}." };
        var projectName = CreateProjectName(
            request.SessionDate,
            request.SessionIdentifier);
        var projectDirectory = Path.Combine(
            request.ProjectOutputDirectory,
            projectName);

        return new LogicProjectPlan(
            projectName,
            projectDirectory,
            Path.Combine(projectDirectory, $"{projectName}.logicx"),
            channels
                .OrderBy(item => item.Channel)
                .Select(item => item.Path)
                .ToArray(),
            warnings);
    }

    public static string CreateProjectName(
        DateOnly sessionDate,
        string sessionIdentifier)
    {
        var safeIdentifier = SanitizeFileName(sessionIdentifier);
        if (string.IsNullOrWhiteSpace(safeIdentifier))
        {
            safeIdentifier = "Unknown";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sessionDate:yyyy-MM-dd}_WingSession_{safeIdentifier}");
    }

    public static string SanitizeFileName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        invalidCharacters.UnionWith(new[]
        {
            '<', '>', ':', '"', '/', '\\', '|', '?', '*'
        });
        var result = new StringBuilder(value.Length);
        var previousWasReplacement = false;

        foreach (var character in value.Trim())
        {
            var replace = invalidCharacters.Contains(character) ||
                char.IsControl(character);
            if (replace)
            {
                if (!previousWasReplacement)
                {
                    result.Append('_');
                }

                previousWasReplacement = true;
                continue;
            }

            result.Append(character);
            previousWasReplacement = false;
        }

        return result.ToString().Trim(' ', '.');
    }

    private static int ParseChannelNumber(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var match = ChannelFileName().Match(Path.GetFileName(path));
        if (!match.Success ||
            !int.TryParse(
                match.Groups["channel"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var channel) ||
            channel < 1)
        {
            throw new InvalidDataException(
                $"Track file does not use the CH<number>.wav convention: {path}");
        }

        return channel;
    }

    private static string FormatChannels(IEnumerable<int> channels) =>
        string.Join(", ", channels.Select(channel => $"CH{channel:00}"));

    [GeneratedRegex(
        "^CH(?<channel>[0-9]+)\\.wav$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChannelFileName();
}
