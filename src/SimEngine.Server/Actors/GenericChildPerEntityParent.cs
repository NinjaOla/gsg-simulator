using Akka.Actor;
using Akka.Cluster.Sharding;

namespace SimEngine.Server.Actors;

/// <summary>
/// A generic "child per entity" parent actor. Routes each message to a child
/// entity actor (creating it on demand), mimicking cluster-sharding semantics
/// without requiring a cluster. Used in <see cref="AkkaExecutionMode.LocalTest"/>
/// for single-player and unit tests.
/// </summary>
public sealed class GenericChildPerEntityParent : ReceiveActor
{
    private readonly IMessageExtractor _extractor;
    private readonly Func<string, Props> _propsFactory;

    /// <summary>Creates the props for a child-per-entity parent.</summary>
    public static Props CreateProps(IMessageExtractor extractor, Func<string, Props> propsFactory) =>
        Props.Create(() => new GenericChildPerEntityParent(extractor, propsFactory));

    public GenericChildPerEntityParent(IMessageExtractor extractor, Func<string, Props> propsFactory)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(propsFactory);
        _extractor = extractor;
        _propsFactory = propsFactory;

        ReceiveAny(message =>
        {
            var entityId = _extractor.EntityId(message);
            if (entityId is null)
            {
                return;
            }

            Context.Child(entityId)
                .GetOrElse(() => Context.ActorOf(_propsFactory(entityId), entityId))
                .Forward(_extractor.EntityMessage(message));
        });
    }
}
