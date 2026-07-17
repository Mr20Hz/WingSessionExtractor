using Avalonia.Platform.Storage;

namespace WingSessionExtractor.Gui;

public interface IFolderPicker
{
    Task<string?> PickAsync(string title, string currentDirectory);
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
