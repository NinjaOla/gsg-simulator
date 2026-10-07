namespace SimEngine.Server.Gateway;

/// <summary>Resolves client-supplied save names to server paths.</summary>
internal static class SavePaths
{
    private const string DefaultExtension = ".json";

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Throws <see cref="ArgumentException"/> for names that escape the save directory.</summary>
    public static string Resolve(string requested, GatewayOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requested);
        var directory = Path.GetFullPath(options.SaveDirectory);

        if (options.AllowArbitrarySavePaths)
        {
            return WithExtension(Path.GetFullPath(Path.IsPathRooted(requested) ? requested : Path.Combine(directory, requested)));
        }

        if (!string.Equals(requested, Path.GetFileName(requested), StringComparison.Ordinal)
            || requested.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || requested.Trim('.', ' ').Length == 0
            || ReservedNames.Contains(Path.GetFileNameWithoutExtension(requested)))
        {
            throw new ArgumentException($"'{requested}' is not a valid save name. Use a plain file name.", nameof(requested));
        }

        var fullPath = WithExtension(Path.GetFullPath(Path.Combine(directory, requested)));
        if (!string.Equals(Path.GetDirectoryName(fullPath), Path.TrimEndingDirectorySeparator(directory), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"'{requested}' is not a valid save name. Use a plain file name.", nameof(requested));
        }

        return fullPath;
    }

    private static string WithExtension(string path) =>
        string.IsNullOrEmpty(Path.GetExtension(path)) ? path + DefaultExtension : path;
}
