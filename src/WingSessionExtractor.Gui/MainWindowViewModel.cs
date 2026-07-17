using System.ComponentModel;
using System.Runtime.CompilerServices;
using WingSessionExtractor.Application;

namespace WingSessionExtractor.Gui;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IExportRunner exportRunner;
    private readonly IFolderPicker folderPicker;
    private readonly IDirectorySettingsStore settingsStore;
    private CancellationTokenSource? cancellationTokenSource;
    private string inputDirectory;
    private string outputDirectory;
    private double progressValue;
    private string statusText = "Select input and output directories to begin.";
    private string errorMessage = "";
    private bool isRunning;

    public MainWindowViewModel(
        IExportRunner exportRunner,
        IFolderPicker folderPicker,
        IDirectorySettingsStore settingsStore)
    {
        this.exportRunner = exportRunner;
        this.folderPicker = folderPicker;
        this.settingsStore = settingsStore;

        var settings = settingsStore.Load();
        inputDirectory = settings.InputDirectory;
        outputDirectory = settings.OutputDirectory;

        StartCommand = new AsyncDelegateCommand(
            StartExportAsync,
            () => CanStart);
        CancelCommand = new DelegateCommand(CancelExport, () => IsRunning);
        PickInputCommand = new AsyncDelegateCommand(
            PickInputDirectoryAsync,
            () => CanEdit);
        PickOutputCommand = new AsyncDelegateCommand(
            PickOutputDirectoryAsync,
            () => CanEdit);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncDelegateCommand StartCommand { get; }

    public DelegateCommand CancelCommand { get; }

    public AsyncDelegateCommand PickInputCommand { get; }

    public AsyncDelegateCommand PickOutputCommand { get; }

    public string InputDirectory
    {
        get => inputDirectory;
        set
        {
            if (Set(ref inputDirectory, value))
            {
                OnPropertyChanged(nameof(CanStart));
                StartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string OutputDirectory
    {
        get => outputDirectory;
        set
        {
            if (Set(ref outputDirectory, value))
            {
                OnPropertyChanged(nameof(CanStart));
                StartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public double ProgressValue
    {
        get => progressValue;
        private set => Set(ref progressValue, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => Set(ref statusText, value);
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (Set(ref errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool IsRunning
    {
        get => isRunning;
        private set
        {
            if (!Set(ref isRunning, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanStart));
            StartCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
            PickInputCommand.RaiseCanExecuteChanged();
            PickOutputCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanEdit => !IsRunning;

    public bool CanStart =>
        !IsRunning &&
        !string.IsNullOrWhiteSpace(InputDirectory) &&
        !string.IsNullOrWhiteSpace(OutputDirectory);

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public async Task StartExportAsync()
    {
        if (!CanStart)
        {
            return;
        }

        ErrorMessage = "";
        ProgressValue = 0;
        StatusText = "Scanning sessions…";
        IsRunning = true;
        SaveSettings();

        using var cancellation = new CancellationTokenSource();
        cancellationTokenSource = cancellation;

        try
        {
            var progress = new Progress<ExportProgress>(ReportProgress);
            await exportRunner.ExportAsync(
                InputDirectory,
                OutputDirectory,
                progress,
                cancellation.Token);

            ProgressValue = 100;
            StatusText = "Export completed.";
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested)
        {
            StatusText = "Export cancelled.";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            StatusText = "Export failed.";
        }
        finally
        {
            cancellationTokenSource = null;
            IsRunning = false;
        }
    }

    public void CancelExport()
    {
        if (cancellationTokenSource is null)
        {
            return;
        }

        StatusText = "Cancelling…";
        cancellationTokenSource.Cancel();
    }

    private async Task PickInputDirectoryAsync()
    {
        await PickDirectoryAsync(
            "Select WING recording directory",
            InputDirectory,
            value => InputDirectory = value);
    }

    private async Task PickOutputDirectoryAsync()
    {
        await PickDirectoryAsync(
            "Select output directory",
            OutputDirectory,
            value => OutputDirectory = value);
    }

    private async Task PickDirectoryAsync(
        string title,
        string currentDirectory,
        Action<string> apply)
    {
        try
        {
            ErrorMessage = "";
            var selected = await folderPicker.PickAsync(title, currentDirectory);
            if (selected is null)
            {
                return;
            }

            apply(selected);
            SaveSettings();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private void ReportProgress(ExportProgress progress)
    {
        ProgressValue = progress.TotalFrames == 0
            ? 100
            : progress.FramesProcessed * 100.0 / progress.TotalFrames;
        StatusText =
            $"Exporting session {progress.SessionId} " +
            $"({progress.SessionIndex}/{progress.SessionCount}) — " +
            $"{ProgressValue:0.0}%";
    }

    private void SaveSettings()
    {
        try
        {
            settingsStore.Save(new DirectorySettings(
                InputDirectory,
                OutputDirectory));
        }
        catch (IOException exception)
        {
            ErrorMessage = $"Could not save directory settings: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            ErrorMessage = $"Could not save directory settings: {exception.Message}";
        }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
