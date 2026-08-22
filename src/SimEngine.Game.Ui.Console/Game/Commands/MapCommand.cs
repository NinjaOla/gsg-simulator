using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using SimEngine;
using SimEngine.Game;
using SimEngine.Rendering;
using SimEngine.Server.Worlds;
using Spectre.Console;

namespace SimEngine.Game.Ui.Console.Game.Commands;

/// <summary>
/// Renders the current world's provinces to a PNG and opens it in the OS
/// image viewer. Province polygons are not kept in engine state, so the map
/// is rendered from the world's source GeoJSON (resolved via the world id
/// stored in engine metadata).
/// </summary>
public sealed class MapCommand : ICommand
{
    private const int DefaultWidth = 1600;

    public string Name => "map";
    public string[] Aliases => ["m"];
    public string Description => "Render the world's provinces to a PNG and open it.";
    public string Usage => "map [width]";

    public void Execute(GameSession session, string[] args)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(args);

        var width = DefaultWidth;
        if (args.Length > 0 && (!int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out width) || width < 256))
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] map [[width]]  [dim](width >= 256)[/]");
            return;
        }

        if (!session.Engine.State.Metadata.TryGetValue(GameManifestMetadata.ScenarioIdKey, out var worldId)
            || string.IsNullOrWhiteSpace(worldId))
        {
            AnsiConsole.MarkupLine("[red]Cannot render:[/] the active world id is unknown.");
            return;
        }

        var asset = WorldCatalog.Find(worldId);
        if (asset is null)
        {
            AnsiConsole.MarkupLine($"[red]Cannot render:[/] no catalog entry for world [yellow]{Markup.Escape(worldId)}[/].");
            return;
        }

        var geoJsonPath = WorldCatalog.ResolvePath(asset);
        if (!File.Exists(geoJsonPath))
        {
            AnsiConsole.MarkupLine($"[red]Cannot render:[/] GeoJSON not found at [dim]{Markup.Escape(geoJsonPath)}[/].");
            return;
        }

        var outputPath = GameTempDirectory.GetPath(
            $"map-{worldId}-{DateTime.Now:yyyyMMdd-HHmmss}.png");

        var marineRegionsPath = WorldCatalog.ResolveMarineRegionsPath();
        var marineOverlay = File.Exists(marineRegionsPath) ? marineRegionsPath : null;

        try
        {
            GeoJsonMapRenderer.RenderFileToPng(geoJsonPath, outputPath, new MapRenderOptions { Width = width }, marineOverlay);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]Render failed:[/] {Markup.Escape(ex.Message)}");
            return;
        }

        AnsiConsole.MarkupLine($"[green]Rendered[/] {session.ProvinceCount} provinces to [dim]{Markup.Escape(outputPath)}[/]");
        TryOpen(outputPath);
    }

    private static void TryOpen(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", path);
            }
            else
            {
                Process.Start("xdg-open", path);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            AnsiConsole.MarkupLine($"[dim]Open the file manually: {Markup.Escape(path)}[/]");
        }
    }
}




