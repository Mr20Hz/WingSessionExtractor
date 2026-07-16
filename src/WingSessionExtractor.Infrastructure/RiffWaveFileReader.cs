using System.Text;
using WingSessionExtractor.Application;
using WingSessionExtractor.Domain;

namespace WingSessionExtractor.Infrastructure;

public sealed class RiffWaveFileReader : IWaveFileReader
{
    public SessionSegment Read(string sessionId, string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        var container = FourCc(reader);
        _ = reader.ReadUInt32();
        var wave = FourCc(reader);

        if (container is not ("RIFF" or "RF64") || wave != "WAVE")
        {
            throw new InvalidDataException($"Unsupported WAV: {path}");
        }

        ulong? rf64DataLength = null;
        WaveFormat? format = null;
        long? dataOffset = null;
        long? dataLength = null;

        while (stream.Position + 8 <= stream.Length)
        {
            var id = FourCc(reader);
            var size32 = reader.ReadUInt32();
            var start = stream.Position;
            long size;

            if (id == "ds64")
            {
                if (size32 < 28)
                {
                    throw new InvalidDataException($"Invalid ds64: {path}");
                }

                _ = reader.ReadUInt64();
                rf64DataLength = reader.ReadUInt64();
                _ = reader.ReadUInt64();
                _ = reader.ReadUInt32();
                size = size32;
            }
            else
            {
                size = container == "RF64" && id == "data" && size32 == uint.MaxValue
                    ? checked((long)(rf64DataLength ??
                        throw new InvalidDataException($"RF64 without ds64: {path}")))
                    : size32;

                if (id == "fmt ")
                {
                    if (size < 16)
                    {
                        throw new InvalidDataException($"Invalid fmt: {path}");
                    }

                    var audioFormat = reader.ReadUInt16();
                    var channels = reader.ReadUInt16();
                    var sampleRate = reader.ReadUInt32();
                    _ = reader.ReadUInt32();
                    var blockAlign = reader.ReadUInt16();
                    var bits = reader.ReadUInt16();
                    format = new WaveFormat(
                        audioFormat, channels, sampleRate, blockAlign, bits);
                }
                else if (id == "data")
                {
                    dataOffset = stream.Position;
                    dataLength = size;
                }
            }

            stream.Position = Math.Min(
                checked(start + size + (size & 1)),
                stream.Length);
        }

        if (format is null || dataOffset is null || dataLength is null)
        {
            throw new InvalidDataException($"Incomplete WAV: {path}");
        }

        _ = format.BytesPerSample;

        if (dataLength.Value % format.BlockAlign != 0)
        {
            throw new InvalidDataException($"Unaligned audio data: {path}");
        }

        return new SessionSegment(
            sessionId,
            path,
            format,
            dataOffset.Value,
            dataLength.Value);
    }

    private static string FourCc(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        if (bytes.Length != 4)
        {
            throw new EndOfStreamException();
        }

        return Encoding.ASCII.GetString(bytes);
    }
}
