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

        // Body: the world bytes, streamed so large worlds stay allocation-light.
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            int read;
            while ((read = worldContent.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer.AsSpan(0, read));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
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
}
