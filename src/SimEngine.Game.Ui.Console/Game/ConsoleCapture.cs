using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace SimEngine.Game.Ui.Console.Game;

/// <summary>
/// Renders Spectre output into ANSI-styled lines instead of the terminal, so
/// the live game screen can place it inside its own frame.
/// </summary>
internal static class ConsoleCapture
{
    /// <summary>Runs <paramref name="action"/> with <see cref="AnsiConsole"/> redirected and returns its output lines.</summary>
    public static string[] Run(Action action, int width)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var original = AnsiConsole.Console;
        AnsiConsole.Console = Create(writer, width);
        try
        {
            action();
        }
        finally
        {
            AnsiConsole.Console = original;
        }

        return ToLines(writer);
    }

    /// <summary>Renders <paramref name="renderable"/> at <paramref name="width"/> columns.</summary>
    public static string[] Render(IRenderable renderable, int width)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Create(writer, width).Write(renderable);
        return ToLines(writer);
    }

    private static IAnsiConsole Create(TextWriter writer, int width)
    {
        var capabilities = AnsiConsole.Profile.Capabilities;
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = capabilities.Ansi ? AnsiSupport.Yes : AnsiSupport.No,
            ColorSystem = (ColorSystemSupport)(int)capabilities.ColorSystem,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = width;
        console.Profile.Capabilities.Unicode = capabilities.Unicode;
        return console;
    }

    private static string[] ToLines(StringWriter writer)
    {
        var lines = writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return [.. lines];
    }
}
