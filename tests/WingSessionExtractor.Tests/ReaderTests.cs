using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WingSessionExtractor.Infrastructure;

namespace WingSessionExtractor.Tests;

[TestClass]
public sealed class ReaderTests
{
    [TestMethod]
    public void Read_ParsesRf64WithDs64()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RF64"));
                writer.Write(uint.MaxValue);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));

                writer.Write(Encoding.ASCII.GetBytes("ds64"));
                writer.Write(28u);
                writer.Write(0UL); // riff size
                writer.Write(16UL); // data size
                writer.Write(2UL); // frame count
                writer.Write(0u); // table length

                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16u);
                writer.Write((ushort)1);
                writer.Write((ushort)2);
                writer.Write(48000u);
                writer.Write(384000u);
                writer.Write((ushort)8);
                writer.Write((ushort)32);

                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(uint.MaxValue);
                writer.Write(new byte[16]);
            }

            var segment = new RiffWaveFileReader().Read("1", path);
            Assert.AreEqual(16L, segment.DataLength);
            Assert.AreEqual(2L, segment.FrameCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidDataException))]
    public void Read_ThrowsOnUnsupportedContainer()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes("NOTARIFFAAAAAAAAWAVE"));
            new RiffWaveFileReader().Read("1", path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidDataException))]
    public void Read_ThrowsOnRf64WithoutDs64()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RF64"));
                writer.Write(uint.MaxValue);
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
                writer.Write(uint.MaxValue);
            }
            new RiffWaveFileReader().Read("1", path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidDataException))]
    public void Read_ThrowsOnUnalignedData()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(44u);
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
                writer.Write(7u); // Not multiple of 8 (block align)
                writer.Write(new byte[7]);
            }
            new RiffWaveFileReader().Read("1", path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidDataException))]
    public void Read_ThrowsOnIncompleteFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVE")); // Size says 0, but it is too short
            new RiffWaveFileReader().Read("1", path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidDataException))]
    public void Read_ThrowsOnMissingFmtChunk()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(12u);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(0u);
            }
            new RiffWaveFileReader().Read("1", path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
