using System.Globalization;
using SimEngine.Contracts;

namespace SimEngine.Server;

/// <summary>
/// Stores <see cref="GameSettings"/> in the game state metadata, so saves
/// carry the host's settings and a loaded game restores them.
/// </summary>
public static class SessionSaveSettings
{
    private const string PauseControlKey = "session.pauseControl";
    private const string SpeedControlKey = "session.speedControl";
    private const string AllowDebugCommandsKey = "session.allowDebugCommands";
    private const string InitialSpeedKey = "session.initialSpeed";

    public static void Write(IDictionary<string, string> metadata, GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(settings);

        metadata[PauseControlKey] = settings.PauseControl.ToString();
        metadata[SpeedControlKey] = settings.SpeedControl.ToString();
        metadata[AllowDebugCommandsKey] = settings.AllowDebugCommands ? "true" : "false";
        metadata[InitialSpeedKey] = settings.InitialSpeed.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Reads stored settings; missing or invalid values fall back to the defaults.</summary>
    public static GameSettings Read(IDictionary<string, string> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var settings = new GameSettings();
        if (TryGetAccess(metadata, PauseControlKey, out var pause))
        {
            settings = settings with { PauseControl = pause };
        }

        if (TryGetAccess(metadata, SpeedControlKey, out var speedControl))
        {
            settings = settings with { SpeedControl = speedControl };
        }

        if (metadata.TryGetValue(AllowDebugCommandsKey, out var debug) && bool.TryParse(debug, out var allowDebug))
        {
            settings = settings with { AllowDebugCommands = allowDebug };
        }

        if (metadata.TryGetValue(InitialSpeedKey, out var speedText)
            && int.TryParse(speedText, NumberStyles.None, CultureInfo.InvariantCulture, out var speed)
            && GameSettings.IsValidSpeed(speed))
        {
            settings = settings with { InitialSpeed = speed };
        }

        return settings;
    }

    private static bool TryGetAccess(IDictionary<string, string> metadata, string key, out ControlAccess access)
    {
        access = default;
        return metadata.TryGetValue(key, out var text)
            && Enum.TryParse(text, ignoreCase: false, out access)
            && Enum.IsDefined(access);
    }
}
