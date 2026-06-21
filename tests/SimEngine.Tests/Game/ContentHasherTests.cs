using System.Text;
using SimEngine.Game;
using Xunit;

namespace SimEngine.Tests.Game;

public sealed class ContentHasherTests
{
    private const string Version = "dev";

    private readonly ITestOutputHelper _output;

    public ContentHasherTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static Stream Bytes(string content) =>
        new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public void Compute_SameInputs_ProducesSameHash()
    {
        var first = ContentHasher.Compute(Bytes("world-bytes"), Version, ["mod-a", "mod-b"]);
        var second = ContentHasher.Compute(Bytes("world-bytes"), Version, ["mod-a", "mod-b"]);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Compute_DifferentWorldBytes_ProducesDifferentHash()
    {
        var original = ContentHasher.Compute(Bytes("world-one"), Version);
        var modified = ContentHasher.Compute(Bytes("world-two"), Version);

        Assert.NotEqual(original, modified);
    }

    [Fact]
    public void Compute_DifferentContentVersion_ProducesDifferentHash()
    {
        var first = ContentHasher.Compute(Bytes("world"), "v1");
        var second = ContentHasher.Compute(Bytes("world"), "v2");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Compute_DifferentEnabledFeatures_ProducesDifferentHash()
    {
        var withoutMod = ContentHasher.Compute(Bytes("world"), Version, []);
        var withMod = ContentHasher.Compute(Bytes("world"), Version, ["mod-a"]);

        Assert.NotEqual(withoutMod, withMod);
    }

    [Fact]
    public void Compute_FeatureOrder_DoesNotAffectHash()
    {
        var ascending = ContentHasher.Compute(Bytes("world"), Version, ["mod-a", "mod-b"]);
        var descending = ContentHasher.Compute(Bytes("world"), Version, ["mod-b", "mod-a"]);

        Assert.Equal(ascending, descending);
    }

    [Fact]
    public void Compute_FeatureBoundaries_AreUnambiguous()
    {
        // Length-prefixing must keep ["ab"] distinct from ["a", "b"].
        var single = ContentHasher.Compute(Bytes("world"), Version, ["ab"]);
        var split = ContentHasher.Compute(Bytes("world"), Version, ["a", "b"]);

        Assert.NotEqual(single, split);
    }

    [Fact]
    public void Compute_NullFeatures_EqualsEmptyFeatures()
    {
        var nullFeatures = ContentHasher.Compute(Bytes("world"), Version, enabledFeatures: null);
        var emptyFeatures = ContentHasher.Compute(Bytes("world"), Version, []);

        Assert.Equal(nullFeatures, emptyFeatures);
    }

    [Fact]
    public void Compute_ReturnsLowercaseHexSha256Digest()
    {
        var hash = ContentHasher.Compute(Bytes("world"), Version);

        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void Compute_NullStream_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ContentHasher.Compute(null!, Version));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Compute_BlankContentVersion_Throws(string contentVersion)
    {
        Assert.Throws<ArgumentException>(
            () => ContentHasher.Compute(Bytes("world"), contentVersion));
    }

    [Fact]
    public void ComputeFromFile_MatchesComputeOverSameBytes()
    {
        const string content = "geojson-world-content";
        var path = Path.Combine(Path.GetTempPath(), $"simengine-content-{Guid.NewGuid():N}.json");
        _output.WriteLine($"Temp world file: {path}");
        File.WriteAllText(path, content);

        var fromFile = ContentHasher.ComputeFromFile(path, Version, ["mod-a"]);
        var fromStream = ContentHasher.Compute(Bytes(content), Version, ["mod-a"]);

        Assert.Equal(fromStream, fromFile);
    }

    [Fact]
    public void ComputeFromFile_BlankPath_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => ContentHasher.ComputeFromFile("  ", Version));
    }
}
