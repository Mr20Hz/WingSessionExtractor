using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WingSessionExtractor.Application;
using WingSessionExtractor.Infrastructure;

namespace WingSessionExtractor.Gui;

public sealed partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var source = new FileSystemSessionSource(new RiffWaveFileReader());
            var exportService = new ExportService(
                source,
                new InterleavedChannelExporter());

            var window = new MainWindow();
            var folderPicker = new AvaloniaFolderPicker(
                () => window.StorageProvider);

            window.DataContext = new MainWindowViewModel(
                new SharedServiceExportRunner(exportService),
                folderPicker,
                new JsonDirectorySettingsStore());
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
