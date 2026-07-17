using WingSessionExtractor.Application;

namespace WingSessionExtractor.Gui;

public interface IExportRunner
{
    Task ExportAsync(
        string inputDirectory,
        string outputDirectory,
        IProgress<ExportProgress> progress,
        CancellationToken cancellationToken);
}

public sealed class SharedServiceExportRunner(ExportService exportService)
    : IExportRunner
{
    public Task ExportAsync(
        string inputDirectory,
        string outputDirectory,
        IProgress<ExportProgress> progress,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => exportService.Export(
                inputDirectory,
                "00000001.WAV",
                new ExportRequest(outputDirectory),
                progress,
                cancellationToken),
            cancellationToken);
}
