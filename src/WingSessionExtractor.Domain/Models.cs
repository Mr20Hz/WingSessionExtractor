namespace WingSessionExtractor.Domain;

public sealed record WaveFormat(
    ushort AudioFormat,
    ushort Channels,
    uint SampleRate,
    ushort BlockAlign,
    ushort BitsPerSample)
{
    public int BytesPerSample =>
        Channels > 0 && BlockAlign % Channels == 0
            ? BlockAlign / Channels
            : throw new InvalidOperationException("Invalid block alignment.");
}

public sealed record SessionSegment(
    string SessionId,
    string FilePath,
    WaveFormat Format,
    long DataOffset,
    long DataLength)
{
    public long FrameCount => DataLength / Format.BlockAlign;
    public TimeSpan Duration =>
        TimeSpan.FromSeconds((double)FrameCount / Format.SampleRate);
}

public sealed record InspectionReport(
    IReadOnlyList<SessionSegment> Segments,
    WaveFormat Format,
    long TotalFrames)
{
    public TimeSpan TotalDuration =>
        TimeSpan.FromSeconds((double)TotalFrames / Format.SampleRate);
}
