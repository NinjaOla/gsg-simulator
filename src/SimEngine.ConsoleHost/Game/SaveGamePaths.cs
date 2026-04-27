namespace SimEngine.ConsoleHost.Game;

public static class SaveGamePaths
{
    /// <summary>Default directory for relative save paths in the console host.</summary>
    public static string SavesDirectory => Path.Combine(AppContext.BaseDirectory, "saves");

    /// <summary>Resolves a save path against the console host save directory.</summary>
    public static string Resolve(string path) => Resolve(path, SavesDirectory);

    /// <summary>Resolves a save path against a specific save directory.</summary>
    public static string Resolve(string path, string savesDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or whitespace.", nameof(path));
        }

        if (string.IsNullOrWhiteSpace(savesDirectory))
        {
            throw new ArgumentException("Save directory cannot be null or whitespace.", nameof(savesDirectory));
        }

        var candidate = Path.IsPathRooted(path)
            ? path
            : Path.Combine(savesDirectory, path);

        var fullPath = Path.GetFullPath(candidate);
        return string.IsNullOrEmpty(Path.GetExtension(fullPath))
            ? fullPath + ".json"
            : fullPath;
    }

    /// <summary>Lists discovered save files from the default console host save directory.</summary>
    public static IReadOnlyList<string> List() => List(SavesDirectory);

    /// <summary>Lists discovered save files from a specific save directory.</summary>
    public static IReadOnlyList<string> List(string savesDirectory)
    {
        if (string.IsNullOrWhiteSpace(savesDirectory))
        {
            throw new ArgumentException("Save directory cannot be null or whitespace.", nameof(savesDirectory));
        }

        if (!Directory.Exists(savesDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(savesDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Gets the display name used by the console host for a save file.</summary>
    public static string GetDisplayName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or whitespace.", nameof(path));
        }

        return Path.GetFileNameWithoutExtension(Path.GetFullPath(path));
    }
}
