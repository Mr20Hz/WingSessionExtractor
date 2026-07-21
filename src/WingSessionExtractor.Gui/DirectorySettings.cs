using System.Text.Json;

namespace WingSessionExtractor.Gui;

public sealed record DirectorySettings(
    string InputDirectory = "",
    string OutputDirectory = "",
    bool EnableLogicProjectCreation = false,
    string LogicTemplatePath = "",
    string LogicProjectOutputDirectory = "");

public interface IDirectorySettingsStore
{
    DirectorySettings Load();

    void Save(DirectorySettings settings);
}

public sealed class JsonDirectorySettingsStore : IDirectorySettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string path;

    public JsonDirectorySettingsStore(string? path = null)
    {
        this.path = path ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "WingSessionExtractor",
            "settings.json");
    }

    public DirectorySettings Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return new DirectorySettings();
            }

            return JsonSerializer.Deserialize<DirectorySettings>(
                       File.ReadAllText(path),
                       SerializerOptions)
                   ?? new DirectorySettings();
        }
        catch (IOException)
        {
            return new DirectorySettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new DirectorySettings();
        }
        catch (JsonException)
        {
            return new DirectorySettings();
        }
    }

    public void Save(DirectorySettings settings)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = path + ".partial";
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(settings, SerializerOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
