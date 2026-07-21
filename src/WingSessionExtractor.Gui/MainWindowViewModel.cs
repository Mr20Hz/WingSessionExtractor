using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using WingSessionExtractor.Application;

namespace WingSessionExtractor.Gui;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IWorkflowRunner workflowRunner;
    private readonly IFolderPicker folderPicker;
    private readonly IDirectorySettingsStore settingsStore;
    private CancellationTokenSource? cancellationTokenSource;
    private string inputDirectory;
    private string outputDirectory;
    private double overallProgress;
    private double stepProgress;
    private string currentStep = "Not started";
    private string statusText = "Select input and output directories to begin.";
    private string statusLogText = "";
    private string finalSummary = "";
    private string errorMessage = "";
    private WorkflowExecutionState executionState = WorkflowExecutionState.Idle;

    public MainWindowViewModel(
        IWorkflowRunner workflowRunner,
        IFolderPicker folderPicker,
        IDirectorySettingsStore settingsStore)
    {
        this.workflowRunner = workflowRunner;
        this.folderPicker = folderPicker;
        this.settingsStore = settingsStore;

        var settings = settingsStore.Load();
        inputDirectory = settings.InputDirectory;
        outputDirectory = settings.OutputDirectory;
        WorkflowSteps = workflowRunner.Steps;

        StartCommand = new AsyncDelegateCommand(
            StartWorkflowAsync,
            () => CanStart);
        CancelCommand = new DelegateCommand(CancelWorkflow, () => CanCancel);
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

    public IReadOnlyList<WorkflowStepDescriptor> WorkflowSteps { get; }

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

    public double OverallProgress
    {
        get => overallProgress;
        private set => Set(ref overallProgress, value);
    }

    public double StepProgress
    {
        get => stepProgress;
        private set => Set(ref stepProgress, value);
    }

    public string CurrentStep
    {
        get => currentStep;
        private set => Set(ref currentStep, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => Set(ref statusText, value);
    }

    public string StatusLogText
    {
        get => statusLogText;
        private set => Set(ref statusLogText, value);
    }

    public string FinalSummary
    {
        get => finalSummary;
        private set
        {
            if (Set(ref finalSummary, value))
            {
                OnPropertyChanged(nameof(HasFinalSummary));
            }
        }
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

    public WorkflowExecutionState ExecutionState
    {
        get => executionState;
        private set
        {
            if (!Set(ref executionState, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsCancelling));
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanCancel));
            StartCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
            PickInputCommand.RaiseCanExecuteChanged();
            PickOutputCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsRunning =>
        ExecutionState is WorkflowExecutionState.Running or
            WorkflowExecutionState.Cancelling;

    public bool IsCancelling =>
        ExecutionState == WorkflowExecutionState.Cancelling;

    public bool CanEdit => !IsRunning;

    public bool CanStart =>
        CanEdit &&
        !string.IsNullOrWhiteSpace(InputDirectory) &&
        !string.IsNullOrWhiteSpace(OutputDirectory);

    public bool CanCancel =>
        ExecutionState == WorkflowExecutionState.Running;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasFinalSummary => !string.IsNullOrWhiteSpace(FinalSummary);

    public async Task StartWorkflowAsync()
    {
        if (!CanStart)
        {
            return;
        }

        ErrorMessage = "";
        FinalSummary = "";
        StatusLogText = "";
        OverallProgress = 0;
        StepProgress = 0;
        CurrentStep = "Preparing workflow";
        StatusText = "Starting workflow…";
        ExecutionState = WorkflowExecutionState.Running;
        SaveSettings();

        using var cancellation = new CancellationTokenSource();
        cancellationTokenSource = cancellation;

        try
        {
            var context = new WorkflowContext(InputDirectory, OutputDirectory);
            var progress = new Progress<WorkflowProgress>(ReportProgress);
            var result = await workflowRunner.RunAsync(
                context,
                progress,
                cancellation.Token);

            ApplyResult(result);
        }
        catch (Exception exception)
        {
            ExecutionState = WorkflowExecutionState.Failed;
            ErrorMessage = exception.Message;
            StatusText = "Workflow failed.";
            AppendLog($"Unexpected GUI workflow error: {exception}");
            FinalSummary = $"Failed — {exception.Message}";
        }
        finally
        {
            cancellationTokenSource = null;
        }
    }

    public void CancelWorkflow()
    {
        if (!CanCancel || cancellationTokenSource is null)
        {
            return;
        }

        ExecutionState = WorkflowExecutionState.Cancelling;
        StatusText = "Cancelling…";
        AppendLog("Cancellation requested.");
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

    private void ReportProgress(WorkflowProgress progress)
    {
        CurrentStep = progress.CurrentStepDisplayName;
        OverallProgress = progress.OverallPercentage;
        StepProgress = progress.StepPercentage;
        StatusText = string.IsNullOrWhiteSpace(progress.Message)
            ? $"Running {progress.CurrentStepDisplayName}…"
            : progress.Message;
        AppendLog(
            $"[{progress.CurrentStepIndex}/{progress.StepCount}] " +
            $"{progress.CurrentStepDisplayName}: {StatusText}");
    }

    private void ApplyResult(WorkflowResult result)
    {
        ExecutionState = result.State;
        if (result.State == WorkflowExecutionState.Completed)
        {
            OverallProgress = 100;
            StepProgress = 100;
            StatusText = "Workflow completed.";
        }
        else if (result.State == WorkflowExecutionState.Cancelled)
        {
            StatusText = "Workflow cancelled.";
        }
        else
        {
            StatusText = "Workflow failed.";
            ErrorMessage = result.StepResults.LastOrDefault()?.Message
                ?? result.Message;
        }

        AppendLog(result.Message);
        FinalSummary = BuildSummary(result);
    }

    private static string BuildSummary(WorkflowResult result)
    {
        var summary = new StringBuilder();
        summary.AppendLine($"Result: {result.State}");
        summary.AppendLine($"Duration: {result.Duration.TotalSeconds:0.0} seconds");
        summary.AppendLine(
            $"Extracted tracks: {result.Context.ExtractedTrackFiles.Count}");

        foreach (var step in result.StepResults)
        {
            summary.AppendLine(
                $"{step.StepDisplayName}: {step.State} " +
                $"({step.Duration.TotalSeconds:0.0} seconds)" +
                (string.IsNullOrWhiteSpace(step.Message)
                    ? ""
                    : $" — {step.Message}"));
        }

        return summary.ToString().TrimEnd();
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        StatusLogText = string.IsNullOrEmpty(StatusLogText)
            ? message
            : $"{StatusLogText}{Environment.NewLine}{message}";
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
