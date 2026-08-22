# gameengine.Renderer.Stride

Code-only Stride renderer client for the simulation stack.

## Current scope

	- `Worlds/grid4.geojson` (default for fast startup)
	- `Worlds/world_admin1.geojson` (full dataset)

## Run

 Shows a top-left info window with loaded dataset and latest pick result.

## Camera controls

- Hold right mouse button and drag: orbit around globe
- Mouse wheel: zoom in or out
- Left click: pick globe lon/lat and print inside/nearest province lookup to console

## Hybrid editor workflow

This project now includes a Stride asset package so you can open and edit a scene in Stride Editor:

- Package: `src/gameengine.Renderer.Stride/gameengine.Renderer.Stride.sdpkg`
- Scene: `src/gameengine.Renderer.Stride/Assets/MainScene.sdscene`
- Game settings: `src/gameengine.Renderer.Stride/Assets/GameSettings.sdgamesettings`

In Stride Editor, open the `.sdpkg` file, then open `MainScene.sdscene`.

Runtime code in `Program.cs` still creates the sphere and loads GeoJSON in code; this keeps your data/render logic code-driven while camera/light defaults are editable in the scene asset.

Load the bundled full-world borders:

```powershell
dotnet run --project src/gameengine.Renderer.Stride/gameengine.Renderer.Stride.csproj -- --full-world
```

Load a custom GeoJSON path:

```powershell
dotnet run --project src/gameengine.Renderer.Stride/gameengine.Renderer.Stride.csproj -- --geojson data\europe-west\europe_west_admin1.geojson
```

## Next integration step

Add camera orbit and picking so clicking the globe can map screen coordinates to lon/lat and then to a province.
