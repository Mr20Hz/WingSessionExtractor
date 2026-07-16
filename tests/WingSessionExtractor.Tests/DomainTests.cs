using Microsoft.VisualStudio.TestTools.UnitTesting;
using WingSessionExtractor.Domain;

namespace WingSessionExtractor.Tests;

[TestClass]
public sealed class DomainTests
{
    [TestMethod]
    public void WaveFormat_BytesPerSample_CalculatesCorrectly()
    {
        var format = new WaveFormat(1, 2, 48000, 8, 32);
        Assert.AreEqual(4, format.BytesPerSample);
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public void WaveFormat_BytesPerSample_ThrowsOnZeroChannels()
    {
        var format = new WaveFormat(1, 0, 48000, 8, 32);
        _ = format.BytesPerSample;
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public void WaveFormat_BytesPerSample_ThrowsOnInvalidAlignment()
    {
        var format = new WaveFormat(1, 3, 48000, 8, 32);
        _ = format.BytesPerSample;
    }

    [TestMethod]
    public void SessionSegment_CalculatesCorrectly()
    {
        var format = new WaveFormat(1, 2, 48000, 8, 32);
        var segment = new SessionSegment("1", "path", format, 44, 16);

        Assert.AreEqual(2L, segment.FrameCount);
        Assert.AreEqual(TimeSpan.FromSeconds(2.0 / 48000), segment.Duration);
    }

    [TestMethod]
    public void InspectionReport_CalculatesCorrectly()
    {
        var format = new WaveFormat(1, 2, 48000, 8, 32);
        var segment1 = new SessionSegment("1", "path1", format, 44, 16);
        var segment2 = new SessionSegment("1", "path2", format, 44, 16);
        var report = new InspectionReport(new[] { segment1, segment2 }, format, 4);

        Assert.AreEqual(TimeSpan.FromSeconds(4.0 / 48000), report.TotalDuration);
    }
}
