using Microsoft.VisualStudio.TestTools.UnitTesting;
using WingSessionExtractor.Application;
using WingSessionExtractor.Gui;

namespace WingSessionExtractor.Tests;

[TestClass]
public sealed class GuiTests
{
    [TestMethod]
    public void SettingsStore_RoundTripsDirectories()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");

        try
        {
            var store = new JsonDirectorySettingsStore(path);
            var expected = new DirectorySettings("input", "output");

            store.Save(expected);

            Assert.AreEqual(expected, store.Load());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task ViewModel_ExportsUsingSelectedDirectories()
    {
        var runner = new RecordingExportRunner();
        var settings = new MemorySettingsStore(
            new DirectorySettings("remembered-input", "remembered-output"));
        var viewModel = new MainWindowViewModel(
            runner,
            new NullFolderPicker(),
            settings);

        await viewModel.StartExportAsync();

        Assert.AreEqual("remembered-input", runner.InputDirectory);
        Assert.AreEqual("remembered-output", runner.OutputDirectory);
        Assert.AreEqual("Export completed.", viewModel.StatusText);
        Assert.AreEqual(100d, viewModel.ProgressValue);
        Assert.IsFalse(viewModel.IsRunning);
        Assert.AreEqual(
            new DirectorySettings("remembered-input", "remembered-output"),
            settings.Settings);
    }

    [TestMethod]
    public async Task ViewModel_CancelsRunningExport()
    {
        var viewModel = new MainWindowViewModel(
            new WaitForCancellationExportRunner(),
            new NullFolderPicker(),
            new MemorySettingsStore(new DirectorySettings("input", "output")));

        var export = viewModel.StartExportAsync();
        Assert.IsTrue(viewModel.IsRunning);

        viewModel.CancelExport();
        await export;

        Assert.AreEqual("Export cancelled.", viewModel.StatusText);
        Assert.IsFalse(viewModel.IsRunning);
        Assert.IsFalse(viewModel.HasError);
    }

    [TestMethod]
    public async Task ViewModel_DisplaysExportErrors()
    {
        var viewModel = new MainWindowViewModel(
            new FailingExportRunner(),
            new NullFolderPicker(),
            new MemorySettingsStore(new DirectorySettings("input", "output")));

        await viewModel.StartExportAsync();

        Assert.AreEqual("Export failed.", viewModel.StatusText);
        Assert.AreEqual("Test failure", viewModel.ErrorMessage);
        Assert.IsTrue(viewModel.HasError);
    }

    private sealed class RecordingExportRunner : IExportRunner
    {
        public string? InputDirectory { get; private set; }

        public string? OutputDirectory { get; private set; }

        public Task ExportAsync(
            string inputDirectory,
            string outputDirectory,
            IProgress<ExportProgress> progress,
            CancellationToken cancellationToken)
        {
            InputDirectory = inputDirectory;
            OutputDirectory = outputDirectory;
            return Task.CompletedTask;
        }
    }

    private sealed class WaitForCancellationExportRunner : IExportRunner
    {
        public Task ExportAsync(
            string inputDirectory,
            string outputDirectory,
            IProgress<ExportProgress> progress,
            CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class FailingExportRunner : IExportRunner
    {
        public Task ExportAsync(
            string inputDirectory,
            string outputDirectory,
            IProgress<ExportProgress> progress,
            CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("Test failure"));
    }

    private sealed class NullFolderPicker : IFolderPicker
    {
        public Task<string?> PickAsync(
            string title,
            string currentDirectory) =>
            Task.FromResult<string?>(null);
    }

    private sealed class MemorySettingsStore(DirectorySettings settings)
        : IDirectorySettingsStore
    {
        public DirectorySettings Settings { get; private set; } = settings;

        public DirectorySettings Load() => Settings;

        public void Save(DirectorySettings value) => Settings = value;
    }
}
