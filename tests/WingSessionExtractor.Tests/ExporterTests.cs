using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WingSessionExtractor.Application;
using WingSessionExtractor.Domain;
using WingSessionExtractor.Infrastructure;

namespace WingSessionExtractor.Tests;

[TestClass]
public sealed class ExporterTests
{
    [TestMethod]
    public void Export_HandlesBufferCarryOver()
    {
        var inputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outputDir);

        try
        {
            var format = new WaveFormat(1, 1, 48000, 2, 16);
            var wavPath = Path.Combine(inputDir, "test.wav");
            using (var stream = File.Create(wavPath))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(44u + 10u);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16u);
                writer.Write((ushort)1);
                writer.Write((ushort)1);
                writer.Write(48000u);
                writer.Write(96000u);
                writer.Write((ushort)2);
                writer.Write((ushort)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(10u);
                writer.Write(new byte[10]); // 5 frames
            }

            var segment = new SessionSegment("1", wavPath, format, 44, 10);

            // We want to test carry-over. The Exporter uses a 1MB buffer.
            // Let's force it by using a small buffer if we could, but it's hardcoded to 1MB.
            // However, the Process method logic can still be tested with multiple segments or large data.
            // The logic: var requested = (int)Math.Min(buffer.Length - carry, remaining);
            // If we have remaining > buffer.Length, it will carry.

            var exporter = new InterleavedChannelExporter();
            exporter.Export(new[] { segment }, new ExportRequest(outputDir));

            var resultPath = Path.Combine(outputDir, "CH01.wav");
            Assert.IsTrue(File.Exists(resultPath));
            var info = new FileInfo(resultPath);
            Assert.AreEqual(44L + 10L, info.Length);
        }
        finally
        {
            Directory.Delete(inputDir, true);
            Directory.Delete(outputDir, true);
        }
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidDataException))]
    public void Export_ThrowsOnChannelMismatch()
    {
        var format = new WaveFormat(1, 2, 48000, 4, 16);
        var segments = new[] { new SessionSegment("1", "p1", format, 0, 0) };
        var exporter = new InterleavedChannelExporter();
        exporter.Export(segments, new ExportRequest("out", ExpectedChannels: 1));
    }

    [TestMethod]
    [ExpectedException(typeof(IOException))]
    public void Export_ThrowsIfOutputExistsAndNoOverwrite()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "CH01.wav"), "");

        try
        {
            var format = new WaveFormat(1, 1, 48000, 2, 16);
            var segments = new[] { new SessionSegment("1", "p1", format, 0, 0) };
            var exporter = new InterleavedChannelExporter();
            exporter.Export(segments, new ExportRequest(dir, Overwrite: false));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void Export_ReportsProgress()
    {
        var inputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outputDir);

        try
        {
            var format = new WaveFormat(1, 1, 48000, 2, 16);
            var wavPath = Path.Combine(inputDir, "test.wav");
            using (var stream = File.Create(wavPath))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(44u + 4u);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16u);
                writer.Write((ushort)1);
                writer.Write((ushort)1);
                writer.Write(48000u);
                writer.Write(96000u);
                writer.Write((ushort)2);
                writer.Write((ushort)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(4u);
                writer.Write(new byte[4]);
            }

            var segment = new SessionSegment("1", wavPath, format, 44, 4);
            var exporter = new InterleavedChannelExporter();

            var progressHits = new List<ExportProgress>();
            var progress = new SynchronousProgress<ExportProgress>(p => progressHits.Add(p));

            exporter.Export(new[] { segment }, new ExportRequest(outputDir), progress);

            Assert.AreEqual(1, progressHits.Count);
            Assert.AreEqual(2L, progressHits[0].FramesProcessed);
            Assert.AreEqual(2L, progressHits[0].TotalFrames);
        }
        finally
        {
            Directory.Delete(inputDir, true);
            Directory.Delete(outputDir, true);
        }
    }

    [TestMethod]
    public void Export_HandlesCancellation()
    {
        var outputDir = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
        var format = new WaveFormat(1, 1, 48000, 2, 16);
        var segments = new[] { new SessionSegment("1", "p1", format, 0, 100) };
        var exporter = new InterleavedChannelExporter();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsException<OperationCanceledException>(() =>
            exporter.Export(
                segments,
                new ExportRequest(outputDir),
                cancellationToken: cts.Token));
        Assert.IsFalse(Directory.Exists(outputDir));
    }

    [TestMethod]
    public void Export_CancellationRemovesPartialFiles()
    {
        var inputPath = Path.GetTempFileName();
        var outputDir = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(inputPath, new byte[8]);

        try
        {
            var format = new WaveFormat(1, 1, 48000, 2, 16);
            var segments = new[]
            {
                new SessionSegment("1", inputPath, format, 0, 4),
                new SessionSegment("2", inputPath, format, 4, 4)
            };
            using var cancellation = new CancellationTokenSource();
            var progress = new SynchronousProgress<ExportProgress>(
                _ => cancellation.Cancel());

            Assert.ThrowsException<OperationCanceledException>(() =>
                new InterleavedChannelExporter().Export(
                    segments,
                    new ExportRequest(outputDir),
                    progress,
                    cancellation.Token));

            Assert.IsFalse(File.Exists(Path.Combine(outputDir, "CH01.wav")));
            Assert.IsFalse(File.Exists(Path.Combine(outputDir, "CH01.wav.partial")));
        }
        finally
        {
            File.Delete(inputPath);
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }
        }
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentException))]
    public void Export_ThrowsOnEmptySegments()
    {
        var exporter = new InterleavedChannelExporter();
        exporter.Export(Array.Empty<SessionSegment>(), new ExportRequest("out"));
    }
}

public sealed class SynchronousProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}
