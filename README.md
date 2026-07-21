# WingSessionExtractor

Cross-platform .NET 8 CLI and Avalonia desktop application for converting Behringer WING-LIVE interleaved session recordings into continuous mono WAV tracks.

## Rider

Open `WingSessionExtractor.slnx`.

Run configuration for inspection:

```text
inspect --input "/path/to/WING/rawsd1"
```

Run configuration for export:

```text
export --input "/path/to/WING/rawsd1" --output "/path/to/output" --channels 16 --overwrite
```

## Build

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

## Desktop application

Start the portable GUI on Windows, Linux, or macOS with:

```bash
dotnet run --project src/WingSessionExtractor.Gui
```

Select the WING recording directory and an output directory, then choose **Start Workflow**. The application remembers both directories. An in-progress workflow can be stopped with **Cancel**; temporary `.partial` files are removed when cancellation completes.

### GUI-driven workflow

The desktop application is the manually started workflow orchestrator. It runs
the configured Application-layer steps sequentially, displays current-step and
overall progress, and keeps the final result visible after completion, failure,
or cancellation. The first workflow uses the existing extraction service
in-process to validate the input, create the selected output directory, and
extract tracks. There is no daemon, watcher, automatic startup, or automatic
termination. See [GUI workflow orchestrator](docs/workflow-orchestrator.md) for
the architecture and extension model.

### Logic Pro integration

On macOS, the GUI can optionally copy a user-selected Logic project template,
open the copy in Logic Pro, import extracted mono tracks in numeric channel
order, and save the new project. Logic settings remain unavailable on Windows
and Linux. Accessibility permission is required for the isolated UI automation
used by Logic's import dialog. See [Logic Pro integration](docs/logic-integration.md)
for template requirements, permissions, timeouts, and troubleshooting.

## Features

- RIFF and RF64 parsing
- actual `data` chunk detection; no 44-byte-header assumption
- hexadecimal session ordering
- automatic channel count from the WAV header
- interleaved PCM demultiplexing
- continuous `CH01.wav`, `CH02.wav`, ... output
- RF64 output above 4 GiB
- `.partial` files for safe export
- portable Avalonia GUI for Windows, Linux, and macOS

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
