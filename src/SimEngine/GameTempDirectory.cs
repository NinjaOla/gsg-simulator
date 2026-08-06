namespace SimEngine;

/// <summary>
/// Central location for the engine's temporary files. Everything the game or
/// engine writes to disk transiently (rendered maps, scratch exports, etc.)
/// should live under a single, well-known folder so it is easy to find and
/// clean up.
/// </summary>
public static class GameTempDirectory
{
    /// <summary>Name of the engine's temp folder under the OS temp path.</summary>
    public const string FolderName = "gsg";

    /// <summary>
    /// Absolute path to the engine temp folder (e.g. <c>%TEMP%\gsg</c>).
    /// The directory is created on first access if it does not exist.
    /// </summary>
    public static string Root
    {
        get
        {
            var root = Path.Combine(Path.GetTempPath(), FolderName);
            Directory.CreateDirectory(root);
            return root;
        }
    }

    /// <summary>
    /// Returns a path under the engine temp folder, creating the folder if
    /// needed. Pass path segments (e.g. a file name) to combine with the root.
    /// </summary>
    public static string GetPath(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var root = Root;
        return segments.Length == 0 ? root : Path.Combine([root, .. segments]);
    }
}
