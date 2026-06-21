namespace SimEngine.Contracts;

/// <summary>
/// Thrown when a client's content hash does not match the session's
/// authoritative hash, indicating incompatible static data (world map/geography
/// or mods). The server rejects the join to protect deterministic lockstep:
/// participants that disagree on static content cannot stay in sync.
/// </summary>
[GenerateSerializer]
public sealed class ContentMismatchException : Exception
{
    /// <summary>
    /// Creates the exception for a join rejected because
    /// <paramref name="actualHash"/> (the client's content hash) differs from
    /// <paramref name="expectedHash"/> (the session's content hash).
    /// </summary>
    public ContentMismatchException(string expectedHash, string actualHash)
        : base(BuildMessage(expectedHash, actualHash))
    {
        ExpectedHash = expectedHash;
        ActualHash = actualHash;
    }

    /// <summary>The session's authoritative content hash.</summary>
    [Id(0)]
    public string ExpectedHash { get; }

    /// <summary>The content hash the client presented on join.</summary>
    [Id(1)]
    public string ActualHash { get; }

    private static string BuildMessage(string expectedHash, string actualHash) =>
        $"Content hash mismatch: the session expects '{expectedHash}' but the client " +
        $"presented '{actualHash}'. Ensure the same world and mods are installed.";
}
