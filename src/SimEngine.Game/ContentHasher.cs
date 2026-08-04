using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SimEngine.Game;

/// <summary>
/// Computes a deterministic content hash over the static data that must match
/// across all participants for deterministic lockstep: the loaded world file
/// bytes plus the content version and enabled features (mods), mirroring the
/// <see cref="GameManifest"/> triad. Each client computes this locally from its
/// own content and presents it on join; the server rejects mismatches.
/// </summary>
public static class ContentHasher
{
    private const int CopyBufferSize = 81920;

    /// <summary>
    /// Computes the content hash from a world file on disk plus the content
    /// version and enabled features. Returns a lowercase hex SHA-256 string.
    /// </summary>
    public static string ComputeFromFile(
        string worldFilePath,
        string contentVersion,
        IReadOnlyList<string>? enabledFeatures = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldFilePath);

        using var stream = new FileStream(
            worldFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        return Compute(stream, contentVersion, enabledFeatures);
    }

    /// <summary>
    /// Computes the content hash from multiple deterministic-content files
    /// (for example, provinces plus countries) plus content version/features.
    /// File order does not affect the hash.
    /// </summary>
    public static string ComputeFromFiles(
        IReadOnlyList<string> filePaths,
        string contentVersion,
        IReadOnlyList<string>? enabledFeatures = null)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        if (filePaths.Count == 0)
        {
            throw new ArgumentException("At least one file path is required.", nameof(filePaths));
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHeader(hash, contentVersion, enabledFeatures);

        var orderedPaths = filePaths
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        AppendInt32(hash, orderedPaths.Length);

        foreach (var filePath in orderedPaths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            AppendToken(hash, Path.GetFileName(filePath));

            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            AppendInt64(hash, stream.Length);
            AppendStream(hash, stream);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// Computes the content hash from a world content stream plus the content
    /// version and enabled features. Reads <paramref name="worldContent"/> from
    /// its current position to the end. Returns a lowercase hex SHA-256 string.
    /// </summary>
    public static string Compute(
        Stream worldContent,
        string contentVersion,
        IReadOnlyList<string>? enabledFeatures = null)
    {
        ArgumentNullException.ThrowIfNull(worldContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentVersion);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHeader(hash, contentVersion, enabledFeatures);
        AppendInt32(hash, 1);
        AppendToken(hash, "world");
        AppendInt64(hash, worldContent.CanSeek ? worldContent.Length - worldContent.Position : -1);
        AppendStream(hash, worldContent);

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendHeader(
        IncrementalHash hash,
        string contentVersion,
        IReadOnlyList<string>? enabledFeatures)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentVersion);

        // Header: content version then ordinal-sorted features, each
        // length-prefixed so distinct inputs cannot collide through
        // concatenation (e.g. ["ab"] vs ["a", "b"]).
        AppendToken(hash, contentVersion);

        var features = (enabledFeatures ?? [])
            .OrderBy(feature => feature, StringComparer.Ordinal)
            .ToArray();
        AppendInt32(hash, features.Length);
        foreach (var feature in features)
        {
            AppendToken(hash, feature);
        }
    }

    private static void AppendStream(IncrementalHash hash, Stream content)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            int read;
            while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer.AsSpan(0, read));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void AppendToken(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        AppendInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        hash.AppendData(buffer);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        hash.AppendData(buffer);
    }
}
