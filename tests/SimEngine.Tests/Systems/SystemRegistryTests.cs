using SimEngine.Systems;
using SimEngine.Tests.TestSupport;
using Xunit;

namespace SimEngine.Tests.Systems;

public sealed class SystemRegistryTests
{
    [Fact]
    public void Add_ReturnsSameInstanceForChaining()
    {
        var registry = new SystemRegistry();
        var result = registry
            .Add(new RecordingSystem("A"))
            .Add(new RecordingSystem("B"));
        Assert.Same(registry, result);
        Assert.Equal(2, registry.Systems.Count);
    }

    [Fact]
    public void Add_NullSystem_Throws()
    {
        var registry = new SystemRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Add(null!));
    }
}


