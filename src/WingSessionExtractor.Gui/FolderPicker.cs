using Avalonia.Platform.Storage;

namespace WingSessionExtractor.Gui;

public interface IFolderPicker
{
    Task<string?> PickAsync(string title, string currentDirectory);
}

public interface IFilePicker
{
    Task<string?> PickAsync(
        string title,
        string currentPath,
        IReadOnlyList<string> patterns);
}

public sealed class AvaloniaFolderPicker(
    Func<IStorageProvider> storageProviderFactory) : IFolderPicker
{
    public async Task<string?> PickAsync(
        string title,
        string currentDirectory)
    {
        var storageProvider = storageProviderFactory();
        IStorageFolder? suggestedStartLocation = null;

        if (Directory.Exists(currentDirectory))
        {
            suggestedStartLocation = await storageProvider
                .TryGetFolderFromPathAsync(
                    new Uri(Path.GetFullPath(currentDirectory)));
        }

        var folders = await storageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = suggestedStartLocation
            });

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}

public sealed class AvaloniaFilePicker(
    Func<IStorageProvider> storageProviderFactory) : IFilePicker
{
    public async Task<string?> PickAsync(
        string title,
        string currentPath,
        IReadOnlyList<string> patterns)
    {
        var storageProvider = storageProviderFactory();
        IStorageFolder? suggestedStartLocation = null;
        var currentDirectory = Directory.Exists(currentPath)
            ? currentPath
            : Path.GetDirectoryName(currentPath);

        if (!string.IsNullOrWhiteSpace(currentDirectory) &&
            Directory.Exists(currentDirectory))
        {
            suggestedStartLocation = await storageProvider
                .TryGetFolderFromPathAsync(
                    new Uri(Path.GetFullPath(currentDirectory)));
        }

        var files = await storageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = suggestedStartLocation,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Logic Pro project")
                    {
                        Patterns = patterns
                    }
                }
            });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }
}
