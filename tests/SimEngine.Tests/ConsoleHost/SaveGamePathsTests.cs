using SimEngine.ConsoleHost.Game;
using Xunit;

namespace SimEngine.Tests.ConsoleHost;

public sealed class SaveGamePathsTests
{
    [Fact]
    public void Resolve_RelativePath_UsesSaveDirectoryAndAddsJsonExtension()
    {
        var root = CreateTempDirectory();
        var savesDirectory = Path.Combine(root, "saves");

        var path = SaveGamePaths.Resolve("campaign-1", savesDirectory);

        Assert.Equal(Path.Combine(savesDirectory, "campaign-1.json"), path);
    }

    [Fact]
    public void Resolve_AbsolutePath_PreservesExtension()
    {
        var root = CreateTempDirectory();
        var savesDirectory = Path.Combine(root, "unused");
        var absolutePath = Path.Combine(root, "campaign-2.save.json");

        var path = SaveGamePaths.Resolve(absolutePath, savesDirectory);

        Assert.Equal(Path.GetFullPath(absolutePath), path);
    }

    [Fact]
    public void List_ReturnsJsonFilesSortedByFileName()
    {
        var savesDirectory = Path.Combine(CreateTempDirectory(), "saves");
        Directory.CreateDirectory(savesDirectory);
        File.WriteAllText(Path.Combine(savesDirectory, "bravo.json"), "{}");
        File.WriteAllText(Path.Combine(savesDirectory, "alpha.json"), "{}");
        File.WriteAllText(Path.Combine(savesDirectory, "notes.txt"), string.Empty);

        var files = SaveGamePaths.List(savesDirectory);

        Assert.Equal(
            [
                Path.Combine(savesDirectory, "alpha.json"),
                Path.Combine(savesDirectory, "bravo.json"),
            ],
            files);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "simengine-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
