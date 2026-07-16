using Microsoft.VisualStudio.TestTools.UnitTesting;
using WingSessionExtractor.Application;
using WingSessionExtractor.Domain;
using WingSessionExtractor.Infrastructure;

namespace WingSessionExtractor.Tests;

[TestClass]
public sealed class SourceTests
{
    private sealed class MockReader : IWaveFileReader
    {
        public SessionSegment Read(string sessionId, string path)
        {
            return new SessionSegment(
                sessionId,
                path,
                new WaveFormat(1, 1, 48000, 2, 16),
                0,
                0);
        }
    }

    [TestMethod]
    public void Scan_OrdersByHexThenString()
    {
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp, "A"));
        Directory.CreateDirectory(Path.Combine(temp, "10"));
        Directory.CreateDirectory(Path.Combine(temp, "2"));
        Directory.CreateDirectory(Path.Combine(temp, "ZZ"));

        File.WriteAllText(Path.Combine(temp, "A", "test.wav"), "");
        File.WriteAllText(Path.Combine(temp, "10", "test.wav"), "");
        File.WriteAllText(Path.Combine(temp, "2", "test.wav"), "");
        File.WriteAllText(Path.Combine(temp, "ZZ", "test.wav"), "");

        try
        {
            var source = new FileSystemSessionSource(new MockReader());
            var results = source.Scan(temp, "test.wav");

            Assert.AreEqual(4, results.Count);
            Assert.AreEqual("2", results[0].SessionId);
            Assert.AreEqual("A", results[1].SessionId);
            Assert.AreEqual("10", results[2].SessionId);
            Assert.AreEqual("ZZ", results[3].SessionId); // ZZ is not hex, so ulong.MaxValue
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [TestMethod]
    [ExpectedException(typeof(DirectoryNotFoundException))]
    public void Scan_ThrowsOnMissingDirectory()
    {
        var source = new FileSystemSessionSource(new MockReader());
        source.Scan("/non/existent/path", "test.wav");
    }

    [TestMethod]
    public void ParseHex_WorksCorrectly()
    {
        Assert.AreEqual(10UL, FileSystemSessionSource.ParseHex("A"));
        Assert.AreEqual(16UL, FileSystemSessionSource.ParseHex("10"));
        Assert.AreEqual(ulong.MaxValue, FileSystemSessionSource.ParseHex("ZZ"));
    }
}
