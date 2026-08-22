using Akka.Cluster.Sharding;
using SimEngine.Contracts;

namespace SimEngine.Server.Actors;

/// <summary>
/// Routes <see cref="IWithSessionId"/> messages to the game-session entity actor
/// keyed by <see cref="IWithSessionId.SessionId"/>.
/// </summary>
public sealed class SessionMessageExtractor : HashCodeMessageExtractor
{
    /// <summary>Default shard count for session entities.</summary>
    public const int DefaultShardCount = 50;

    public SessionMessageExtractor(int maxNumberOfShards = DefaultShardCount)
        : base(maxNumberOfShards)
    {
    }

    public override string? EntityId(object message) =>
        message switch
        {
            IWithSessionId msg => msg.SessionId,
            _ => null,
        };
}

/// <summary>
/// Routes <see cref="IWithPlayerId"/> messages to the player entity actor keyed
/// by <see cref="IWithPlayerId.PlayerId"/>.
/// </summary>
public sealed class PlayerMessageExtractor : HashCodeMessageExtractor
{
    /// <summary>Default shard count for player entities.</summary>
    public const int DefaultShardCount = 50;

    public PlayerMessageExtractor(int maxNumberOfShards = DefaultShardCount)
        : base(maxNumberOfShards)
    {
    }

    public override string? EntityId(object message) =>
        message switch
        {
            IWithPlayerId msg => msg.PlayerId,
            _ => null,
        };
}
