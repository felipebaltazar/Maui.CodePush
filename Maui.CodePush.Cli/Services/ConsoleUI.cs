namespace Maui.CodePush.Cli.Services;

public static class ConsoleUI
{
    private const string Purple = "\u001b[38;5;135m";
    private const string Cyan = "\u001b[38;5;81m";
    private const string Green = "\u001b[38;5;114m";
    private const string Yellow = "\u001b[38;5;221m";
    private const string Red = "\u001b[38;5;203m";
    private const string White = "\u001b[38;5;255m";
    private const string Gray = "\u001b[38;5;245m";
    private const string DimGray = "\u001b[38;5;240m";
    private const string Bold = "\u001b[1m";
    private const string Reset = "\u001b[0m";

    private static readonly string[] _spinner = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    public static void PrintBanner()
    {
        const string b = "║";
        var boxWidth = 76;
        var top =    $"  {DimGray}╔{new string('═', boxWidth)}╗{Reset}";
        var empty =  $"  {DimGray}{b}{new string(' ', boxWidth)}{b}{Reset}";
        var bottom = $"  {DimGray}╚{new string('═', boxWidth)}╝{Reset}";

        string[] title =
        [
            @" ██████╗ ██████╗ ██████╗ ███████╗",
            @"██╔════╝██╔═══██╗██╔══██╗██╔════╝",
            @"██║     ██║   ██║██║  ██║█████╗  ",
            @"██║     ██║   ██║██║  ██║██╔══╝  ",
            @"╚██████╗╚██████╔╝██████╔╝███████╗",
            @" ╚═════╝ ╚═════╝ ╚═════╝ ╚══════╝",
        ];

        string[] title2 =
        [
            @"██████╗ ██╗   ██╗███████╗██╗  ██╗",
            @"██╔══██╗██║   ██║██╔════╝██║  ██║",
            @"██████╔╝██║   ██║███████╗███████║",
            @"██╔═══╝ ██║   ██║╚════██║██╔══██║",
            @"██║     ╚██████╔╝███████║██║  ██║",
            @" ╚═╝      ╚═════╝╚══════╝╚═╝  ╚═╝",
        ];

        Console.WriteLine();
        Console.WriteLine(top);
        Console.WriteLine(empty);
        Console.WriteLine(empty);

        // CODE on left, PUSH on right — side by side
        for (var i = 0; i < title.Length; i++)
        {
            var line = $"  {title[i]}  {title2[i]}";
            var pad = boxWidth - line.Length;
            var left = pad / 2;
            var right = pad - left;
            var centered = new string(' ', Math.Max(0, left)) + line + new string(' ', Math.Max(0, right));
            if (centered.Length > boxWidth)
                centered = centered[..boxWidth];
            Console.WriteLine($"  {DimGray}{b}{Reset}{Bold}{Purple}{centered}{Reset}{DimGray}{b}{Reset}");
        }

        Console.WriteLine(empty);

        // Subtitle
        var sub = ".NET MAUI";
        var subPad = (boxWidth - sub.Length) / 2;
        var subLine = new string(' ', subPad) + sub + new string(' ', boxWidth - subPad - sub.Length);
        Console.WriteLine($"  {DimGray}{b}{Reset}{Cyan}{subLine}{Reset}{DimGray}{b}{Reset}");

        Console.WriteLine(empty);

        // Version + tagline
        var versionRaw = "v0.1.0-pre  │  OTA updates — no app store review";
        var vLeft = (boxWidth - versionRaw.Length) / 2;
        var vRight = boxWidth - vLeft - versionRaw.Length;
        Console.WriteLine($"  {DimGray}{b}{Reset}{new string(' ', Math.Max(0, vLeft))}{Gray}v0.1.0-pre  {DimGray}│{Reset}  {DimGray}OTA updates — no app store review{new string(' ', Math.Max(0, vRight))}{Reset}{DimGray}{b}{Reset}");

        Console.WriteLine(empty);

        // Company
        var company = "by Monkseal";
        var cPad = (boxWidth - company.Length) / 2;
        var cLine = new string(' ', cPad) + company + new string(' ', boxWidth - cPad - company.Length);
        Console.WriteLine($"  {DimGray}{b}{Reset}{DimGray}{cLine}{Reset}{DimGray}{b}{Reset}");

        Console.WriteLine(empty);
        Console.WriteLine(bottom);
        Console.WriteLine();
    }

    public static void Info(string message)
    {
        Console.WriteLine($"  {Cyan}●{Reset} {message}");
    }

    public static void Success(string message)
    {
        Console.WriteLine($"  {Green}✔{Reset} {message}");
    }

    public static void Warning(string message)
    {
        Console.WriteLine($"  {Yellow}⚠{Reset} {Yellow}{message}{Reset}");
    }

    public static void Error(string message)
    {
        Console.Error.WriteLine($"  {Red}✖{Reset} {Red}{message}{Reset}");
    }

    public static void Detail(string label, string value)
    {
        Console.WriteLine($"    {Gray}{label}:{Reset} {White}{value}{Reset}");
    }

    public static void Separator()
    {
        Console.WriteLine($"  {DimGray}─────────────────────────────────────────{Reset}");
    }

    public static void Blank() => Console.WriteLine();

    public static async Task<T> SpinnerAsync<T>(string message, Func<Task<T>> action)
    {
        var cts = new CancellationTokenSource();
        var spinnerTask = RunSpinnerAsync(message, cts.Token);

        try
        {
            var result = await action();
            cts.Cancel();
            await spinnerTask;
            ClearLine();
            Success(message);
            return result;
        }
        catch
        {
            cts.Cancel();
            await spinnerTask;
            ClearLine();
            Error(message);
            throw;
        }
    }

    public static async Task SpinnerAsync(string message, Func<Task> action)
    {
        await SpinnerAsync(message, async () => { await action(); return 0; });
    }

    private static async Task RunSpinnerAsync(string message, CancellationToken ct)
    {
        var i = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Console.Write($"\r  {Purple}{_spinner[i % _spinner.Length]}{Reset} {Gray}{message}...{Reset}  ");
                i++;
                await Task.Delay(80, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static void ClearLine()
    {
        try
        {
            var width = Console.IsOutputRedirected ? 80 : Console.BufferWidth;
            Console.Write($"\r{new string(' ', Math.Min(width, 120))}\r");
        }
        catch
        {
            Console.Write($"\r{new string(' ', 80)}\r");
        }
    }

    public static void PrintTable(string[] headers, List<string[]> rows)
    {
        if (rows.Count == 0)
        {
            Info("No items found.");
            return;
        }

        var widths = new int[headers.Length];
        for (var i = 0; i < headers.Length; i++)
            widths[i] = headers[i].Length;

        foreach (var row in rows)
        {
            for (var i = 0; i < row.Length && i < widths.Length; i++)
                widths[i] = Math.Max(widths[i], row[i].Length);
        }

        Blank();
        var headerLine = "  ";
        var separatorLine = "  ";
        for (var i = 0; i < headers.Length; i++)
        {
            headerLine += $"{Bold}{White}{headers[i].PadRight(widths[i] + 2)}{Reset}";
            separatorLine += $"{DimGray}{new string('─', widths[i])}{Reset}  ";
        }
        Console.WriteLine(headerLine);
        Console.WriteLine(separatorLine);

        foreach (var row in rows)
        {
            var line = "  ";
            for (var i = 0; i < row.Length && i < widths.Length; i++)
            {
                var color = i == 0 ? Cyan : Gray;
                line += $"{color}{row[i].PadRight(widths[i] + 2)}{Reset}";
            }
            Console.WriteLine(line);
        }
        Blank();
    }
}
