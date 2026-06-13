using Orleans.Runtime;

namespace SimEngine.Contracts;

/// <summary>
/// Names and ids for the per-session Orleans stream that carries
/// <see cref="SessionStreamUpdate"/> messages from the session grain to
/// connected clients.
/// </summary>
public static class SessionStreams
{
    /// <summary>Stream provider name; silo and clients must configure the same one.</summary>
    public const string ProviderName = "sim";

    /// <summary>Stream namespace for game session updates.</summary>
    public const string Namespace = "session";

    /// <summary>Stream id for a given session/grain key.</summary>
    public static StreamId For(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return StreamId.Create(Namespace, sessionId);
    }
}
