using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WingSessionExtractor.Application;
using WingSessionExtractor.Infrastructure;

namespace WingSessionExtractor.Tests;

[TestClass]
public sealed class SmokeTests
{
    [TestMethod]
    public void Reader_ParsesSimpleWave()
    {
        var path = CreateWave();
        try
        {
            var segment = new RiffWaveFileReader().Read("1", path);
            Assert.AreEqual((ushort)2, segment.Format.Channels);
            Assert.AreEqual(2L, segment.FrameCount);
            Assert.AreEqual(44L, segment.DataOffset);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Exporter_DemultiplexesTwoChannels()
    {
        var input = CreateWave();
        var output = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);

        try
        {
            var segment = new RiffWaveFileReader().Read("1", input);
            new InterleavedChannelExporter().Export(
                new[] { segment },
                new ExportRequest(output, ExpectedChannels: 2));

            var left = File.ReadAllBytes(Path.Combine(output, "CH01.wav"));
            var right = File.ReadAllBytes(Path.Combine(output, "CH02.wav"));

            Assert.AreEqual(0, BitConverter.ToInt32(left, 44));
            Assert.AreEqual(100, BitConverter.ToInt32(left, 48));
            Assert.AreEqual(1, BitConverter.ToInt32(right, 44));
            Assert.AreEqual(101, BitConverter.ToInt32(right, 48));
        }
        finally
        {
            File.Delete(input);
            Directory.Delete(output, true);
        }
    }

    private static string CreateWave()
    {
        var path = Path.GetTempFileName();
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(52u);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16u);
        writer.Write((ushort)1);
        writer.Write((ushort)2);
        writer.Write(48000u);
        writer.Write(384000u);
        writer.Write((ushort)8);
        writer.Write((ushort)32);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(16u);
        writer.Write(0);
        writer.Write(1);
        writer.Write(100);
        writer.Write(101);

        return path;
    }
}
