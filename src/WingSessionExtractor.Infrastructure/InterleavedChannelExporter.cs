using System.Buffers;
using System.Text;
using WingSessionExtractor.Application;
using WingSessionExtractor.Domain;

namespace WingSessionExtractor.Infrastructure;

public sealed class InterleavedChannelExporter : IChannelExporter
{
    public void Export(
        IReadOnlyList<SessionSegment> segments,
        ExportRequest request,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (segments.Count == 0)
        {
            throw new ArgumentException("No segments supplied.", nameof(segments));
        }

        Directory.CreateDirectory(request.OutputDirectory);
        var format = segments[0].Format;

        if (request.ExpectedChannels is { } expected &&
            expected != format.Channels)
        {
            throw new InvalidDataException(
                $"Expected {expected} channels, found {format.Channels}.");
        }

        foreach (var segment in segments.Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            InspectService.EnsureCompatible(
                format,
                segment.Format,
                segment.FilePath);
        }

        var totalFrames = segments.Sum(item => item.FrameCount);
        var streams = new List<FileStream>();
        var partials = new List<string>();

        try
        {
            for (var channel = 0; channel < format.Channels; channel++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var final = Path.Combine(
                    request.OutputDirectory,
                    $"CH{channel + 1:00}.wav");

                if (File.Exists(final) && !request.Overwrite)
                {
                    throw new IOException(
                        $"Output exists: {final}. Use --overwrite.");
                }

                var partial = final + ".partial";
                if (File.Exists(partial))
                {
                    File.Delete(partial);
                }

                partials.Add(partial);
                streams.Add(CreateMono(partial, format, totalFrames));
            }

            var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
            try
            {
                long framesProcessed = 0;

                for (var index = 0; index < segments.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Process(segments[index], streams, buffer, cancellationToken);
                    framesProcessed += segments[index].FrameCount;

                    progress?.Report(new ExportProgress(
                        index + 1,
                        segments.Count,
                        segments[index].SessionId,
                        framesProcessed,
                        totalFrames));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            foreach (var stream in streams)
            {
                stream.Flush(flushToDisk: true);
                stream.Dispose();
            }

            cancellationToken.ThrowIfCancellationRequested();

            for (var channel = 0; channel < partials.Count; channel++)
            {
                var final = Path.Combine(
                    request.OutputDirectory,
                    $"CH{channel + 1:00}.wav");

                File.Move(partials[channel], final, request.Overwrite);
            }
        }
        catch
        {
            foreach (var stream in streams)
            {
                try { stream.Dispose(); } catch { }
            }

            foreach (var partial in partials)
            {
                try
                {
                    if (File.Exists(partial))
                    {
                        File.Delete(partial);
                    }
                }
                catch { }
            }

            throw;
        }
    }

    private static void Process(
        SessionSegment segment,
        IReadOnlyList<FileStream> outputs,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        using var input = File.OpenRead(segment.FilePath);
        input.Position = segment.DataOffset;

        var frameSize = segment.Format.BlockAlign;
        var sampleSize = segment.Format.BytesPerSample;
        long remaining = segment.DataLength;
        var carry = 0;

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var requested = (int)Math.Min(buffer.Length - carry, remaining);
            var read = input.Read(buffer, carry, requested);
            if (read == 0)
            {
                throw new EndOfStreamException(segment.FilePath);
            }

            remaining -= read;
            var available = carry + read;
            var complete = available - available % frameSize;

            for (var offset = 0; offset < complete; offset += frameSize)
            {
                for (var channel = 0; channel < segment.Format.Channels; channel++)
                {
                    outputs[channel].Write(
                        buffer,
                        offset + channel * sampleSize,
                        sampleSize);
                }
            }

            carry = available - complete;
            if (carry > 0)
            {
                Buffer.BlockCopy(buffer, complete, buffer, 0, carry);
            }
        }

        if (carry != 0)
        {
            throw new InvalidDataException(
                $"Incomplete final frame: {segment.FilePath}");
        }
    }

    private static FileStream CreateMono(
        string path,
        WaveFormat format,
        long totalFrames)
    {
        var stream = File.Create(path);
        var blockAlign = checked((ushort)format.BytesPerSample);
        var dataLength = checked(totalFrames * blockAlign);
        var rf64 = dataLength + 36L > uint.MaxValue;

        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        if (rf64)
        {
            FourCc(writer, "RF64");
            writer.Write(uint.MaxValue);
            FourCc(writer, "WAVE");
            FourCc(writer, "ds64");
            writer.Write(28u);
            writer.Write((ulong)(72L + dataLength - 8L));
            writer.Write((ulong)dataLength);
            writer.Write((ulong)totalFrames);
            writer.Write(0u);
        }
        else
        {
            FourCc(writer, "RIFF");
            writer.Write(checked((uint)(36L + dataLength)));
            FourCc(writer, "WAVE");
        }

        FourCc(writer, "fmt ");
        writer.Write(16u);
        writer.Write(format.AudioFormat);
        writer.Write((ushort)1);
        writer.Write(format.SampleRate);
        writer.Write(checked(format.SampleRate * blockAlign));
        writer.Write(blockAlign);
        writer.Write(format.BitsPerSample);
        FourCc(writer, "data");
        writer.Write(rf64 ? uint.MaxValue : checked((uint)dataLength));

        return stream;
    }

    private static void FourCc(BinaryWriter writer, string value) =>
        writer.Write(Encoding.ASCII.GetBytes(value));
}
