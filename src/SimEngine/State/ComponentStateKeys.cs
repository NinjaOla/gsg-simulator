using SimEngine.Systems;

namespace SimEngine.State;

/// <summary>
/// Generates a stable <see cref="StateKey"/> per component type so systems
/// can declare reads/writes without magic strings. The key is cached per
/// closed generic, so repeated calls from a system's <c>Reads</c>/<c>Writes</c>
/// getters allocate nothing after the first use.
/// </summary>
public static class ComponentStateKeys
{
    public static StateKey Of<T>() where T : struct
        => Cache<T>.Key;

    private static class Cache<T> where T : struct
    {
        public static readonly StateKey Key = new($"components/{typeof(T).Name}");
    }
}
