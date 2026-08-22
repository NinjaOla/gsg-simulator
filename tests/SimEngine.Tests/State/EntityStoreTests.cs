using SimEngine.Ids;
using SimEngine.State;
using Xunit;

namespace SimEngine.Tests.State;

public sealed class EntityStoreTests
{
    private readonly struct TagA { public int Value { get; init; } }
    private readonly struct TagB { public string Label { get; init; } }

    [Fact]
    public void Create_AllocatesMonotonicIdsStartingAtOne()
    {
        var store = new EntityStore();
        var a = store.Create();
        var b = store.Create();
        var c = store.Create();
        Assert.Equal(new EntityId(1), a);
        Assert.Equal(new EntityId(2), b);
        Assert.Equal(new EntityId(3), c);
    }

    [Fact]
    public void Create_MakesEntityExist_AndIncrementsCount()
    {
        var store = new EntityStore();
        var id = store.Create();
        Assert.True(store.Exists(id));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Destroy_ReturnsTrueForKnownEntity_AndFalseForUnknown()
    {
        var store = new EntityStore();
        var id = store.Create();
        Assert.True(store.Destroy(id));
        Assert.False(store.Destroy(id));
        Assert.False(store.Destroy(new EntityId(999)));
    }

    [Fact]
    public void Destroy_RemovesEntityFromEveryComponentStore()
    {
        var store = new EntityStore();
        var id = store.Create();
        store.Attach(id, new TagA { Value = 1 });
        store.Attach(id, new TagB { Label = "x" });
        Assert.True(store.Destroy(id));
        Assert.False(store.Has<TagA>(id));
        Assert.False(store.Has<TagB>(id));
    }

    [Fact]
    public void Attach_UnknownEntity_Throws()
    {
        var store = new EntityStore();
        Assert.Throws<KeyNotFoundException>(() =>
            store.Attach(new EntityId(42), new TagA { Value = 1 }));
    }

    [Fact]
    public void AttachDetach_RoundTripAffectsHasAndTryGet()
    {
        var store = new EntityStore();
        var id = store.Create();
        store.Attach(id, new TagA { Value = 7 });
        Assert.True(store.Has<TagA>(id));
        Assert.True(store.TryGet<TagA>(id, out var value));
        Assert.Equal(7, value.Value);

        Assert.True(store.Detach<TagA>(id));
        Assert.False(store.Has<TagA>(id));
        Assert.False(store.TryGet<TagA>(id, out _));
    }

    [Fact]
    public void TryGet_UnknownComponentType_ReturnsFalse()
    {
        var store = new EntityStore();
        var id = store.Create();
        Assert.False(store.TryGet<TagA>(id, out _));
    }

    [Fact]
    public void GetRef_PermitsInPlaceMutation()
    {
        var store = new EntityStore();
        var id = store.Create();
        store.Attach(id, new TagA { Value = 1 });
        ref var component = ref store.GetRef<TagA>(id);
        component = new TagA { Value = 99 };
        Assert.True(store.TryGet<TagA>(id, out var observed));
        Assert.Equal(99, observed.Value);
    }

    [Fact]
    public void GetRef_UnknownEntity_Throws()
    {
        var store = new EntityStore();
        var id = store.Create();
        store.Attach(id, new TagA { Value = 1 });
        Assert.Throws<KeyNotFoundException>(() => store.GetRef<TagA>(new EntityId(123)));
    }

    [Fact]
    public void Query_YieldsAscendingByEntityId_EvenWhenAttachedInReverse()
    {
        var store = new EntityStore();
        var ids = Enumerable.Range(0, 10).Select(_ => store.Create()).ToList();

        // Attach in reverse order.
        for (var i = ids.Count - 1; i >= 0; i--)
        {
            store.Attach(ids[i], new TagA { Value = i });
        }

        var observedIds = store.Query<TagA>().Select(pair => pair.Id).ToList();
        Assert.Equal(ids, observedIds);
    }

    [Fact]
    public void All_YieldsLiveEntitiesAscending_SkippingDestroyed()
    {
        var store = new EntityStore();
        var a = store.Create();
        var b = store.Create();
        var c = store.Create();
        store.Destroy(b);
        Assert.Equal(new[] { a, c }, store.All.ToArray());
    }

    [Fact]
    public void CountOf_ReportsPerTypeComponentPopulation()
    {
        var store = new EntityStore();
        var a = store.Create();
        var b = store.Create();
        store.Attach(a, new TagA { Value = 1 });
        store.Attach(b, new TagA { Value = 2 });
        store.Attach(a, new TagB { Label = "x" });
        Assert.Equal(2, store.CountOf<TagA>());
        Assert.Equal(1, store.CountOf<TagB>());
    }
}


