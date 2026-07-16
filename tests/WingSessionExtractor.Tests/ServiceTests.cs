using Microsoft.VisualStudio.TestTools.UnitTesting;
using WingSessionExtractor.Application;
using WingSessionExtractor.Domain;

namespace WingSessionExtractor.Tests;

[TestClass]
public sealed class ServiceTests
{
    private sealed class MockSessionSource(IEnumerable<SessionSegment> segments) : ISessionSource
    {
        public IReadOnlyList<SessionSegment> Scan(string inputDirectory, string fileName) =>
            segments.ToList();
    }

    private sealed class MockExporter : IChannelExporter
    {
        public List<(IReadOnlyList<SessionSegment>, ExportRequest)> Calls { get; } = new();

        public void Export(IReadOnlyList<SessionSegment> segments, ExportRequest request, IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Calls.Add((segments, request));
        }
    }

    [TestMethod]
    public void Inspect_ReturnsReport()
    {
        var format = new WaveFormat(1, 1, 44100, 2, 16);
        var segments = new[]
        {
            new SessionSegment("1", "p1", format, 0, 100),
            new SessionSegment("2", "p2", format, 0, 200)
        };
        var service = new InspectService(new MockSessionSource(segments));

        var report = service.Inspect("input");

        Assert.AreEqual(2, report.Segments.Count);
        Assert.AreEqual(format, report.Format);
        Assert.AreEqual(150L, report.TotalFrames); // 100/2 + 200/2
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public void Inspect_ThrowsOnNoSegments()
    {
        var service = new InspectService(new MockSessionSource(Enumerable.Empty<SessionSegment>()));
        service.Inspect("input");
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidDataException))]
    public void Inspect_ThrowsOnIncompatibleFormats()
    {
        var format1 = new WaveFormat(1, 1, 44100, 2, 16);
        var format2 = new WaveFormat(1, 2, 44100, 4, 16);
        var segments = new[]
        {
            new SessionSegment("1", "p1", format1, 0, 100),
            new SessionSegment("2", "p2", format2, 0, 200)
        };
        var service = new InspectService(new MockSessionSource(segments));
        service.Inspect("input");
    }

    [TestMethod]
    public void Export_CallsExporter()
    {
        var format = new WaveFormat(1, 1, 44100, 2, 16);
        var segments = new[] { new SessionSegment("1", "p1", format, 0, 100) };
        var exporter = new MockExporter();
        var service = new ExportService(new MockSessionSource(segments), exporter);
        var request = new ExportRequest("out");

        service.Export("in", "file", request);

        Assert.AreEqual(1, exporter.Calls.Count);
        Assert.AreSame(request, exporter.Calls[0].Item2);
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public void Export_ThrowsOnNoSegments()
    {
        var exporter = new MockExporter();
        var service = new ExportService(new MockSessionSource(Enumerable.Empty<SessionSegment>()), exporter);
        service.Export("in", "file", new ExportRequest("out"));
    }
}
