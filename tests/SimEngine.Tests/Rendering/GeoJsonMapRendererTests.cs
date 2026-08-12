using SimEngine;
using SimEngine.Rendering;
using Xunit;

namespace SimEngine.Tests.Rendering;

public sealed class GeoJsonMapRendererTests : IDisposable
{
    private static readonly string GermanyGeoJson =
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "germany_admin1.geojson");

    private readonly List<string> _outputPaths = [];

    [Fact]
    public void WhenRenderingGermanyThenPngFileIsCreated()
    {
        var outputPath = CreateOutputPath();

        GeoJsonMapRenderer.RenderFileToPng(GermanyGeoJson, outputPath, new MapRenderOptions { Width = 512 });

        Assert.True(File.Exists(outputPath), $"Expected PNG at {outputPath}");
        Assert.True(new FileInfo(outputPath).Length > 1024, "Expected a non-trivial PNG file.");
    }

    [Fact]
    public void WhenRenderingGermanyThenOutputHasPngSignature()
    {
        var outputPath = CreateOutputPath();

        GeoJsonMapRenderer.RenderFileToPng(GermanyGeoJson, outputPath, new MapRenderOptions { Width = 512 });

        var header = new byte[8];
        using (var stream = File.OpenRead(outputPath))
        {
            var read = stream.Read(header, 0, header.Length);
            Assert.Equal(8, read);
        }

        byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Assert.Equal(pngSignature, header);
    }

    [Fact]
    public void WhenMarineOverlayProvidedThenRenderedOutputChanges()
    {
        const string world = """
            {"type":"FeatureCollection","features":[
              {"type":"Feature","properties":{},"geometry":{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}}
            ]}
            """;
        const string marine = """
            {"type":"FeatureCollection","features":[
              {"type":"Feature","properties":{},"geometry":{"type":"Polygon","coordinates":[[[2,2],[8,2],[8,8],[2,8],[2,2]]]}}
            ]}
            """;
        var options = new MapRenderOptions { Width = 256 };

        var withoutOverlay = RenderToBytes(world, options, marineOverlay: null);
        var withOverlay = RenderToBytes(world, options, marineOverlay: marine);

        Assert.NotEqual(withoutOverlay, withOverlay);
    }

    private static byte[] RenderToBytes(string worldGeoJson, MapRenderOptions options, string? marineOverlay)
    {
        using var world = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(worldGeoJson));
        using var output = new MemoryStream();
        if (marineOverlay is null)
        {
            GeoJsonMapRenderer.Render(world, output, options);
        }
        else
        {
            using var marine = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(marineOverlay));
            GeoJsonMapRenderer.Render(world, output, options, marine);
        }

        return output.ToArray();
    }

    private string CreateOutputPath()
    {
        var path = GameTempDirectory.GetPath($"map-test-{Guid.NewGuid():N}.png");
        _outputPaths.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _outputPaths)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
