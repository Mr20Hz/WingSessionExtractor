using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using WingSessionExtractor.Application;
using WingSessionExtractor.Infrastructure;

namespace WingSessionExtractor.Gui;

public sealed partial class App : Avalonia.Application
{
    private ServiceProvider? services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            services = new ServiceCollection()
                .AddSingleton<IWaveFileReader, RiffWaveFileReader>()
                .AddSingleton<ISessionSource, FileSystemSessionSource>()
                .AddSingleton<IChannelExporter, InterleavedChannelExporter>()
                .AddSingleton<ExportService>()
                .AddSingleton<IWorkflowStep, ExtractTracksWorkflowStep>()
                .AddSingleton<IWorkflowRunner, SequentialWorkflowRunner>()
                .AddSingleton<IDirectorySettingsStore, JsonDirectorySettingsStore>()
                .AddSingleton<IFolderPicker>(_ => new AvaloniaFolderPicker(
                    () => window.StorageProvider))
                .AddSingleton<MainWindowViewModel>()
                .BuildServiceProvider();

            window.DataContext = services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => services.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
