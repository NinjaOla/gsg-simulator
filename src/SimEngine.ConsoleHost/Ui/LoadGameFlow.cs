using SimEngine.ConsoleHost.Game;
using Spectre.Console;

namespace SimEngine.ConsoleHost.Ui;

public static class LoadGameFlow
{
    public static GameSession? Run()
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold]Load Game[/]").RuleStyle("dim grey"));
        AnsiConsole.WriteLine();

        var path = SelectPath();
        if (path is null)
        {
            return null;
        }

        try
        {
            GameSession? session = null;
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start($"Loading {Markup.Escape(path)}...", _ =>
                {
                    session = GameSessionFactory.Load(path);
                });

            if (session is null)
            {
                throw new InvalidOperationException("Load did not produce a game session.");
            }

            AnsiConsole.MarkupLine($"[green]Loaded.[/] {session.ProvinceCount} provinces, tick {session.Engine.TickNumber}.");
            AnsiConsole.MarkupLine("[dim]Press any key to start.[/]");
            Console.ReadKey(intercept: true);
            return session;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            AnsiConsole.MarkupLine($"[red]Could not load save:[/] {Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine("[dim]Press any key.[/]");
            Console.ReadKey(intercept: true);
            return null;
        }
    }

    private static string? SelectPath()
    {
        var saves = SaveGamePaths.List();
        if (saves.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No save files found in the default saves directory.[/]");
            return PromptForPath();
        }

        var choices = saves
            .Select(path => new LoadChoice(
                $"{SaveGamePaths.GetDisplayName(path)}  [dim]({File.GetLastWriteTime(path):yyyy-MM-dd HH:mm})[/]",
                path))
            .Concat([new LoadChoice("Enter path manually", null)])
            .ToArray();

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<LoadChoice>()
                .Title("Select a save:")
                .UseConverter(choice => choice.DisplayName)
                .AddChoices(choices));

        return selected.Path ?? PromptForPath();
    }

    private static string PromptForPath()
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>("Save path:")
                .Validate(path => string.IsNullOrWhiteSpace(path)
                    ? ValidationResult.Error("[red]Enter a save path[/]")
                    : ValidationResult.Success()));
    }

    private sealed record LoadChoice(string DisplayName, string? Path);
}
