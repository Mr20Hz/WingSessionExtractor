# WingSessionExtractor

Cross-platform .NET 8 CLI for converting Behringer WING-LIVE interleaved session recordings into continuous mono WAV tracks.

## Rider

Open `WingSessionExtractor.slnx`.

Run configuration for inspection:

```text
inspect --input "/Users/timbautz/Music/tow/probe210626/rawsd1"
```

Run configuration for export:

```text
export --input "/Users/timbautz/Music/tow/probe210626/rawsd1" --output "/Users/timbautz/Music/WING_JOINED" --channels 16 --overwrite
```

## Build

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

## Features

- RIFF and RF64 parsing
- actual `data` chunk detection; no 44-byte-header assumption
- hexadecimal session ordering
- automatic channel count from the WAV header
- interleaved PCM demultiplexing
- continuous `CH01.wav`, `CH02.wav`, ... output
- RF64 output above 4 GiB
- `.partial` files for safe export
- no runtime NuGet dependencies

## Verified source format

- 16 channels
- 48 kHz
- signed 32-bit PCM
- interleaved
- WING audio data offset at byte 32768

## Current limitations

- no reconstruction of real-time gaps between stopped recordings
- no automatic dual-card classification
- no silent-channel analysis
- no channel-name extraction
