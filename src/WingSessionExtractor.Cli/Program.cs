using WingSessionExtractor.Application;
using WingSessionExtractor.Infrastructure;

namespace WingSessionExtractor.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = Parse(args);

            if (options.ShowHelp)
            {
                Help();
                return 0;
            }

            var source = new FileSystemSessionSource(
                new RiffWaveFileReader());

            if (options.Command == "inspect")
            {
                Print(new InspectService(source).Inspect(
                    options.Input,
                    options.FileName));

                return 0;
            }

            var report = new InspectService(source).Inspect(
                options.Input,
                options.FileName);

            Print(report);
            Console.WriteLine();

            var progress = new Progress<ExportProgress>(item =>
            {
                var percentage = item.TotalFrames == 0
                    ? 100
                    : item.FramesProcessed * 100.0 / item.TotalFrames;

                Console.WriteLine(
                    $"[{item.SessionIndex}/{item.SessionCount}] " +
                    $"{item.SessionId} - {percentage:0.0}%");
            });

            new ExportService(
                source,
                new InterleavedChannelExporter())
                .Export(
                    options.Input,
                    options.FileName,
                    new ExportRequest(
                        options.Output!,
                        options.Overwrite,
                        options.Channels),
                    progress);

            Console.WriteLine();
            Console.WriteLine(
                $"Export completed: {Path.GetFullPath(options.Output!)}");

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static Options Parse(string[] args)
    {
        if (args.Length == 0 ||
            args.Contains("--help") ||
            args.Contains("-h"))
        {
            return new Options("", "", null, "00000001.WAV", null, false, true);
        }

        var command = args[0].ToLowerInvariant();
        if (command is not ("inspect" or "export"))
        {
            throw new ArgumentException(
                "First argument must be inspect or export.");
        }

        string? input = null;
        string? output = null;
        var fileName = "00000001.WAV";
        int? channels = null;
        var overwrite = false;

        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "-i":
                case "--input":
                    input = Next(args, ref index);
                    break;
                case "-o":
                case "--output":
                    output = Next(args, ref index);
                    break;
                case "--file-name":
                    fileName = Next(args, ref index);
                    break;
                case "--channels":
                    channels = int.Parse(Next(args, ref index));
                    break;
                case "--overwrite":
                    overwrite = true;
                    break;
                default:
                    throw new ArgumentException(
                        $"Unknown argument: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("--input is required.");
        }

        if (command == "export" && string.IsNullOrWhiteSpace(output))
        {
            throw new ArgumentException(
                "--output is required for export.");
        }

        return new Options(
            command,
            input,
            output,
            fileName,
            channels,
            overwrite,
            false);
    }

    private static string Next(string[] args, ref int index)
    {
        index++;
        if (index >= args.Length)
        {
            throw new ArgumentException("Missing argument value.");
        }

        return args[index];
    }

    private static void Print(
        WingSessionExtractor.Domain.InspectionReport report)
    {
        Console.WriteLine($"Sessions : {report.Segments.Count}");
        Console.WriteLine(
            $"Format   : {report.Format.Channels} ch, " +
            $"{report.Format.SampleRate} Hz, " +
            $"{report.Format.BitsPerSample} bit");
        Console.WriteLine(
            $"Duration : {report.TotalDuration:hh\\:mm\\:ss\\.fff}");

        foreach (var segment in report.Segments)
        {
            Console.WriteLine(
                $"  {segment.SessionId}  " +
                $"{segment.Duration:hh\\:mm\\:ss\\.fff}");
        }
    }

    private static void Help()
    {
        Console.WriteLine(
            "WingSessionExtractor\n\n" +
            "Inspect:\n" +
            "  wingextract inspect --input \"/path/to/rawsd1\"\n\n" +
            "Export:\n" +
            "  wingextract export --input \"/path/to/rawsd1\" " +
            "--output \"/path/to/output\" --channels 16\n\n" +
            "Options:\n" +
            "  -i, --input <directory>\n" +
            "  -o, --output <directory>\n" +
            "      --file-name <name>\n" +
            "      --channels <count>\n" +
            "      --overwrite\n" +
            "  -h, --help");
    }

    private sealed record Options(
        string Command,
        string Input,
        string? Output,
        string FileName,
        int? Channels,
        bool Overwrite,
        bool ShowHelp);
}
