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
