namespace SimEngine.ConsoleHost.Ui;

public sealed record WorldAsset(string DisplayName, string FileName)
{
    public static readonly IReadOnlyList<WorldAsset> All =
    [
        new("Grid 4  (2x2 synthetic)", "grid4.geojson"),
        new("Germany  (16 Bundeslander)", "germany_admin1.geojson"),
    ];

    public string FullPath => Path.Combine(AppContext.BaseDirectory, "Worlds", FileName);
}
