using System.Text.Json;
using Spectre.Console;

namespace SimEngine.Game.Ui.Console.Game;

/// <summary>Player name remembered in the user settings file (<c>%AppData%/gsg/settings.json</c>).</summary>
internal static class PlayerProfile
{
    private const int MaxNameLength = 24;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "gsg", "settings.json");

    /// <summary>The saved name, or the OS user name if none was saved yet.</summary>
    public static string GetName() => Load().PlayerName is { Length: > 0 } name ? name : DefaultName();

    /// <summary>The saved name; prompts for one (and saves it) the first time.</summary>
    public static string GetOrPromptName()
    {
        var settings = Load();
        if (!string.IsNullOrWhiteSpace(settings.PlayerName))
        {
            AnsiConsole.MarkupLine($"[dim]Playing as[/] [bold]{Markup.Escape(settings.PlayerName)}[/]");
            return settings.PlayerName;
        }

        var name = AnsiConsole.Prompt(
            new TextPrompt<string>("Player name:")
                .DefaultValue(DefaultName())
                .Validate(s => s.Trim().Length is > 0 and <= MaxNameLength
                    ? ValidationResult.Success()
                    : ValidationResult.Error($"[red]Enter 1-{MaxNameLength} characters[/]")))
            .Trim();

        Save(settings with { PlayerName = name });
        return name;
    }

    private static string DefaultName()
    {
        var name = Environment.UserName;
        return string.IsNullOrWhiteSpace(name) ? "player" : name;
    }

    private static UserSettings Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new UserSettings()
                : new UserSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new UserSettings();
        }
    }

    private static void Save(UserSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal: the player is asked again next time.
        }
    }

    private sealed record UserSettings
    {
        public string? PlayerName { get; init; }
    }
}
