# Stride3D Learning Path for a HOI4-Style Globe Renderer: A Prioritized Guide

## TL;DR
- **Study these in order:** the official **C# Beginner → C# Intermediate** tutorial series (from the Stride Launcher), then the **Stride Community Toolkit code-only examples** (Procedural Geometry / MeshBuilder, Raycast, ImGui/Myra UI), then the **Custom Material Shader** and **Custom Effect** samples, and finally **StrideTransformationInstancing** and **XenkoByteSized / XenkoProofOfConcepts** for advanced patterns. Your real bottleneck is 3D fundamentals, not C#, so pair this with an external math/graphics primer (Red Blob Games, LearnOpenGL, Freya Holmér).
- **Stride 4.3 (released 14 November 2025, tag v4.3.0.2507) is current and, per Stride's official announcement, "now fully compatible with .NET 10 and leveraging the latest enhancements in C# 14."** It is MIT-licensed (as of Xenko 3.0) and supported by the .NET Foundation, is actively maintained, and can be consumed as plain NuGet packages code-only (no Game Studio) — a good fit for your "renderer is just another client" architecture. But be warned: Stride has **no official headless mode**, its docs on custom rendering/shaders are thin, and networking samples are small proofs-of-concept.
- **The globe/province problem is the hard part and Stride gives you almost nothing for it out of the box.** You'll build it yourself: triangulate GeoJSON polygons (LibTessDotNet or an Earcut port), project lat/lon to a sphere, and — critically — use an **ID/lookup texture + map-mode approach like HOI4/EU4** so you recolor provinces by updating a small texture per tick rather than rebuilding 4,500 meshes. Read non-Stride references (Red Blob Games, the HOI4 map-modding wiki) for technique.

## Start Here Tomorrow (first 5 concrete things)
1. **Install Stride 4.3 via the Launcher** and create the **C# Beginner** tutorial project (Launcher → New project → Tutorials → C# beginner). Run each scene; watch the accompanying YouTube playlist. This teaches entities, components, `SyncScript`/`AsyncScript`, delta time, input.
2. **Spin up a code-only console project** in parallel (`dotnet new console --framework net10.0`, add `Stride.CommunityToolkit.Windows` + `Stride.CommunityToolkit.Bepu` prerelease). This mirrors how you'll actually consume Stride as a library from your existing solution.
3. **Run the Community Toolkit "Procedural geometry" example** (MeshBuilder triangle/plane/circle) and the **"Create a model from code"** manual page. This is the exact API you'll use to build sphere and province meshes.
4. **Do a 2-3 hour 3D math primer** (vectors, dot/cross product, matrices, coordinate systems) using Red Blob Games and Freya Holmér's videos before you fight the engine.
5. **Prototype lat/lon → sphere position** in a throwaway script: place a few debug spheres at known coordinates (equator, poles, your capital cities) to confirm your projection and coordinate handedness are correct.

## Key Findings

**Stride is a viable but "some assembly required" choice for your project.** Stride 4.3 targets .NET 10 and C# 14, is MIT-licensed, part of the .NET Foundation, and can be referenced purely as NuGet packages and driven code-only. That matches your architecture where game logic lives outside the engine and the renderer is a thin client. The engine gives you solid building blocks for procedural meshes, custom SDSL shaders, runtime texture updates, raycasting, GPU instancing, and a built-in UI system.

**But the specific hard problem — a HOI4-style province globe — is almost entirely on you.** There is no Stride sample that renders a GeoJSON province map on a sphere. You will assemble it from generic primitives (custom vertex/index buffers, an ID-lookup texture, a custom material shader). The best learning materials for the *technique* are non-Stride (HOI4 map-modding wiki, Red Blob Games, globe.gl / three-globe).

**The right architecture pattern is confirmed and well-supported:** run your authoritative simulation as a plain .NET process, have the Stride client receive state on a background thread, push snapshots into a thread-safe queue, and drain that queue in a `SyncScript.Update()` (guaranteed main thread) — or `await Script.NextFrame()` to hop back to the main thread. Never mutate the scene graph from the network thread.

**Warnings up front:** Stride has no official headless mode; the built-in UI system is criticized and many teams roll their own or use ImGui/Myra instead; shader/custom-rendering docs are thin; and the networking examples are lightly-maintained POCs. If you were still choosing an engine, Godot-with-C# or a Veldrid/Silk.NET custom renderer would be lower-friction for a pure thin client — but Stride is a reasonable, fully C# choice and you've already committed.

## Details

### 1. What's actually on the example-projects page and in official samples (verified Aug 2026)

**Official tutorial series** (create from the Launcher; each has a YouTube video):
- **C# Beginner** (https://doc.stride3d.net/latest/en/tutorials/csharpbeginner/index.html) — entities, components, script types, transforms, delta time, cloning, keyboard/mouse input. *Do this first.*
- **C# Intermediate** (https://doc.stride3d.net/latest/en/tutorials/csharpintermediate/index.html) — UI basics, collision triggers, **raycasting**, **async scripts**, scenes, animation, audio, **camera and navigation**. Directly relevant: raycasting, async scripts, camera.
- These are maintained and shipped with the current release; note some older *video* recordings still say "Xenko" but the code is current.

**Built-in game templates** (Launcher): First-Person Shooter, Third-Person Platformer, Top-Down RPG. Historically the FPS/Top-Down RPG templates had a shader compile bug (`ComputeColorConstantColorLink` generic error, issue #1181) on some versions — verify they build on 4.3 before relying on them. Useful mainly for the `BasicCameraController` and animation patterns.

**The "Example Projects" community page** (https://doc.stride3d.net/latest/en/community-resources/example-projects.html) lists, among others:
- **XenkoProofOfConcepts** by Basewq (https://github.com/Basewq/XenkoProofOfConcepts) — a large, high-value collection including **Entity Processor**, **Multiplayer**, **Object Info Renderer** (renders per-object data to a render target — relevant to ID/color picking), **Screen Space Decals with custom RootRenderFeature**, Game Screen Manager. *This is the single most useful community repo for you.*
- **XenkoByteSized** by profan (https://github.com/profan/XenkoByteSized) — bite-sized examples including procedural mesh and a **multi-mesh / combined-meshes** snippet (batching many meshes into one draw call — relevant to 4,500 provinces).
- **Stride-Tessellation** by johang88 (https://github.com/johang88/Stride-Tessellation) — GPU tessellation shaders.
- **Stride3DTutorials** by VaclavElias (https://github.com/VaclavElias/Stride3DTutorials) — code-only, drag-and-drop UI, targeting .NET 5/6 (older but instructive).
- **StrideVoxelScape** (https://github.com/Jarb2104/StrideVoxelScape_v0.1) and the **Marching Cube / mesh-from-compute-shader** demo by Nicogo1705 (https://github.com/Nicogo1705/Stride-Generate-Mesh-from-ComputeShader) — dynamic/procedural mesh generation, including GPU-side.
- An "Old Projects" section flags Paradox/Xenko-era projects (ParadoxCraft, XenkoVoxelGI, various VoxelScapes) that need significant rework for Stride 4.x — treat as read-only reference, not runnable.

**Stride Community Toolkit** (https://stride3d.github.io/stride-community-toolkit/) — actively developed. NuGet `Stride.CommunityToolkit` current prerelease is **1.0.0-preview.62** (last updated 11/16/2025), with dependency `Stride.Core.Assets.CompilerApp (>= 4.3.0.2507)` — i.e. it targets Stride 4.3 / .NET 10. This is where the best *modern, code-only* examples live:
- **Procedural geometry / MeshBuilder** (https://stride3d.github.io/stride-community-toolkit/manual/rendering/mesh-builder.html) — dynamic mesh creation with custom vertex layouts.
- **Raycast** (https://stride3d.github.io/stride-community-toolkit/manual/code-only/examples/raycast.html) and **Raycasting and Camera Focus** — mouse picking, camera focus.
- **ImGui UI** and **Myra UI** examples — data-dense HUD options.

### 2. How each recommendation maps to your specific needs

**Procedural mesh generation (sphere + province polygons).**
- Learn from: the manual's **"Create a model from code"** (https://doc.stride3d.net/latest/en/manual/scripts/create-a-model-from-code.html) which shows the exact `Mesh` / `MeshDraw` / `VertexBufferBinding` / `IndexBufferBinding` pattern; the Toolkit **MeshBuilder** and **Procedural geometry** examples; and **GeometricPrimitive** (Sphere, GeoSphere) for a quick base globe. Use `Buffer.Vertex.New(..., GraphicsResourceUsage.Dynamic)` for meshes you'll update.
- For 4,500 provinces: prefer **one big merged mesh** (or a few, chunked) over 4,500 ModelComponents. See XenkoByteSized's combined-meshes snippet. `GeoSphere.New(radius, tessellation)` gives you an evenly-tessellated base sphere without pole pinching.

**Custom SDSL shaders (borders, political map modes, ID lookup).**
- Learn from: the manual **Custom shaders** (https://doc.stride3d.net/latest/en/manual/graphics/effects-and-shaders/custom-shaders.html) and the **Custom Material Shader** + **Custom Effect** samples (create from Launcher). The particle-materials tutorial shows the `ComputeColor` override pattern. Core dev tebjan's advice (discussion #1396) is the canonical starting point: create the "Custom material shader" and "Custom effect" samples, study "Space Escape" for full material customization, and use the **Stride Shader Explorer** to browse the shipped shader inheritance tree.
- Your map-mode coloring is a natural SDSL job: sample a province-ID texture, use the ID to look up a per-province color in a small palette texture, output the color. This is exactly how Paradox games do it.

**Runtime texture manipulation (recolor provinces per tick).**
- Learn from: the **Texture** API (https://doc.stride3d.net/latest/en/api/Stride.Graphics.Texture.html) — `Texture.New2D(...)` and `Texture.SetData(commandList, data, ...)`. `SetData` must be called from the main thread that owns the GraphicsDevice.
- Pattern: keep a tiny `owner[provinceId] → countryColor` palette texture (e.g. Nx1 R8G8B8A8). On tick, update only the palette (a few KB), not the big province-ID texture or the meshes. The GPU shader does the per-pixel lookup. This is O(provinces) CPU work per tick, trivially cheap.

**Camera for a globe (orbit/trackball, zoom, picking to lat/lon → province ID).**
- Learn from: C# Intermediate "camera and navigation"; the FPS template's `BasicCameraController` (fixed in PR #359); the Toolkit Raycast + Camera Focus examples.
- Two picking approaches:
  1. **Mathematical ray-sphere intersection** (recommended for a globe): build a pick ray from the mouse (discussion #2071 shows `GetPickRay` via inverse view-projection), intersect analytically with the sphere, convert the hit point back to lat/lon, then look up the province. No physics colliders needed — clean and exact for a perfect sphere.
  2. **ID-buffer / color picking**: render province IDs to an offscreen render target (see XenkoProofOfConcepts "Object Info Renderer") and read back the pixel under the cursor. More robust for irregular borders; more plumbing.

**UI for a data-dense strategy HUD.**
- Stride's built-in UI: **UIPage / UILibrary** assets with a UI editor (https://doc.stride3d.net/latest/en/manual/ui/index.html). It works but is widely criticized — see the "Stride.UI overhaul ideas" discussion #2491 listing many open bugs and performance issues. Notably, the **Distant Worlds 2** team wrote on the Stride blog (Apr 9, 2025): *"We initially tried out the built-in user interface elements of Xenko. But for various reasons we decided to instead build our own user interface system. So for UI rendering we took the SpriteBatch class and built some basic controls."* For a data-dense strategy title, plan to evaluate alternatives (or a custom SpriteBatch layer) early.
- Alternatives (from the official UI community page https://doc.stride3d.net/latest/en/community-resources/ui.html): **Myra** (full widget library with MML/XML layouts and a visual designer, strong for HUDs), **StrideCommunity.ImGuiDebug / Toolkit ImGui integration** (immediate-mode, superb for dense debug/data panels and rapid iteration), and **Stridelonia** (Avalonia inside Stride — most powerful data-binding, heaviest). For a HOI4-style dense HUD, **ImGui for tools/debug and Myra (or Stride UI) for the shipping HUD** is a sensible split.

**Driving the render loop from an external tick source (your actor host).**
- Script types (https://doc.stride3d.net/latest/en/manual/scripts/types-of-script.html): `SyncScript.Update()` runs every frame on the main thread; `AsyncScript.Execute()` starts on the main thread and can `await`; `StartupScript.Start()` runs once. `EntityProcessor` is for cache-friendly bulk processing of many components (relevant if you go entity-per-province).
- **Confirmed safe pattern:** your networking/deserialization runs on a background thread and pushes world-state snapshots into a `ConcurrentQueue<T>`; a `SyncScript.Update()` drains the queue and applies state to transforms/textures (guaranteed main thread). The officially documented main-thread marshalling primitive is **`await Script.NextFrame()`** — the docs state that after `await Task.Run(...)` "this method now runs on a thread pool thread instead of the main thread," and `Script.NextFrame()` "yields execution of this method to the main thread ... You can now safely interact with the engine's systems." `Game.Script.AddTask` / `Scheduler.Add` schedule cooperative micro-threads on the main thread. (The `ConcurrentQueue`-drain-in-`Update()` approach is the community-idiomatic complement; `NextFrame()`/`AddTask` are the officially documented primitives.)

**Networking / "dumb client" to an external simulation.**
- The Stride networking community page (https://doc.stride3d.net/latest/en/community-resources/networking.html) lists three directly relevant but small POCs:
  - **Stride.ClientServerSample** (fork by Ethereal77, https://github.com/Ethereal77/Stride.ClientServerSample) — a headless Stride server processing physics raycasts remotely; only ~2 commits, essentially a proof of concept (itself a fork of the original xen2/Xenko.ClientServerSample). Its README explicitly notes there is no built-in `HeadlessGame` type and the server drives the Stride API manually to load a scene.
  - **Stride.Networking.Simple** by manio143 (https://github.com/manio143/Stride.Networking.Simple) — an async `NetworkScript` class; ~5 commits. The standalone server runs *outside* Stride. Author confirms "a fully headless Stride ... right now is not" available. Uses Stride's `[DataContract]` binary serialization; server and client must share identical message class definitions.
  - **ET-Stride** (https://github.com/ly3027929699/ET-Stride) — combines the ET actor-model framework (server) with Stride (client); the most active of the three (~28 commits) but small and documented mostly in Chinese. Its Client/Server/Share/Config split keeps authoritative logic outside the engine — the same shape as your architecture.
- Practical guidance: don't adopt these as frameworks. Use a battle-tested transport (LiteNetLib for UDP, or just TCP/gRPC/SignalR since you're state-syncing not twitch-netcoding) directly, and apply the `ConcurrentQueue → SyncScript.Update()` pattern above. **LiteEntitySystem** (https://github.com/RevenantX/LiteEntitySystem) is a mature server-authoritative ECS with rollback if you want more, though it's not Stride-specific.

**Selection/highlighting of many entities + performance for ~4,500 provinces.**
- **GPU instancing** exists since Stride 4.0: add an `InstancingComponent` to an entity with a model; feed transforms via EntityTransform, UserArray, or UserBuffer (structured GPU buffer). See **StrideTransformationInstancing** (on the models-and-animations community page) and tebjan's instancing project. Community testing (discussion #1797) reports 25,000 simple instanced entities at 60 FPS on an RTX 3060, but also that Stride "doesn't do much for you" on batching beyond instancing — memory-transfer batching matters.
- **For a province map, instancing is usually the wrong model** (provinces are unique shapes, not repeated meshes). Prefer: a single merged province mesh + an ID texture + shader-based selection/highlight (change one value in the palette texture to highlight). This turns "highlight a province" into a 1-pixel texture write, and needs zero per-frame CPU iteration over 4,500 objects.

### 3. Concrete ordered learning path

**Phase 0 — 3D fundamentals OUTSIDE Stride (this is your real bottleneck):**
- Vectors, dot/cross product, matrices, transforms: **Freya Holmér's "Math for Game Devs"** videos; **3Blue1Brown "Essence of Linear Algebra."**
- Coordinate systems, handedness, spherical coordinates, UV mapping, mesh topology: **Red Blob Games** (especially "Delaunay+Voronoi on a sphere" for sphere-meshing intuition) and **LearnOpenGL** (Coordinate Systems, Transformations chapters — concepts transfer even though Stride uses SDSL/DirectX conventions, left-handed, Y-up).
- Shader basics: **The Book of Shaders** (fragment-shader intuition) before SDSL.

**Phase 1 — Stride basics (1-2 weeks):**
1. C# Beginner tutorial (all scenes).
2. C# Intermediate tutorial — focus on raycasting, async scripts, camera/navigation, UI basics.
3. Set up a code-only console project with the Community Toolkit in parallel.

**Phase 2 — the building blocks you need (2-4 weeks):**
4. "Create a model from code" + Toolkit MeshBuilder / Procedural geometry → build a sphere from code, then a single triangulated polygon on the sphere.
5. Custom Material Shader + Custom Effect samples + Shader Explorer → write a trivial `ComputeColor` shader, then an ID-lookup coloring shader.
6. Texture `SetData` → recolor via a palette texture at runtime.
7. Raycast + Camera Focus examples → orbit camera + ray-sphere picking to lat/lon.

**Phase 3 — scale and integration (ongoing):**
8. XenkoByteSized combined-meshes + StrideTransformationInstancing → performance patterns.
9. XenkoProofOfConcepts (Entity Processor, Object Info Renderer, Multiplayer) → ID-buffer picking and the thin-client wiring.
10. Wire the ConcurrentQueue → SyncScript.Update() bridge to your host process; add ImGui/Myra HUD.

### 4. Stride-specific practicalities

- **Project structure:** Game Studio projects use an asset system (`.sdpkg` packages, `.sdproj`/now standard `.csproj`, assets compiled by the asset pipeline). But you can bypass Game Studio entirely and go **code-only**: reference `Stride.Engine` (and `Stride.CommunityToolkit.Windows` which pulls in the required packages), instantiate `new Game()`, call `game.Run(...)`. Official guide: https://stride3d.github.io/stride-community-toolkit/manual/code-only/create-project.html. This is ideal for your "renderer as a library/plugin client" model.
- **Library from an existing solution:** yes — confirmed you can add `Stride.Engine` via NuGet and extend `Stride.Engine.Game`. Historically you may need `Stride.Core.Assets.CompilerApp` and a `RuntimeIdentifier` (e.g. `win-x64`) to resolve native libs/shaders.
- **Headless:** **no first-class headless/windowless mode.** Confirmed by discussion #1368 ("running stride headless") and the networking sample READMEs. A windowless authoritative server requires manually driving the Stride API (à la ClientServerSample) or a custom `GameContext`/`GameWindow`. For you this is fine — your *host* is separate and non-Stride; only the *client* needs a window.
- **Versions:** Stride 4.3 = .NET 10 + C# 14 (Nov 2025). 4.2 = .NET 8 (Feb 2024). So **.NET 10 is supported as of 4.3.** The Toolkit's own docs say: *"If you're still on Stride 4.2, use --version 1.0.0-preview.61 instead of --prerelease, which targets Stride 4.3"* — so use `--prerelease` for 4.3 and pin `preview.61` only if you must stay on 4.2. (Verify against the official docs before pinning, as the version boundary shifts with releases.)
- **Community state 2026:** active but small. Regular releases, an active Discord (with a #toolkit channel), OpenCollective funding, **7.7k GitHub stars** (per the stride3d/stride repo, "Fork 1.1k · Star 7.7k"). Docs are decent for basics/tutorials but **thin for advanced rendering, custom render features, and shaders**. Real shipped games exist — notably **Distant Worlds 2**, a 4X space-strategy game developed by Code Force and published by Slitherine Software (released for Windows March 10, 2022, listed with "Engine: Stride") — encouraging precedent for a strategy title, though it is a space 4X, not a province-map game. Code-only is officially Windows-focused; Linux works but is "more involved."

### 5. The GeoJSON-province-on-a-globe problem (engine-agnostic technique)

This is the crux and Stride won't help directly. Key decisions:

**Approach A — Equirectangular ID texture (recommended, HOI4/EU4 style).**
- Bake your provinces into a **province-ID texture** (each province a unique color/ID) in equirectangular (plate carrée) layout — lon→U, lat→V. Wrap it on a standard UV sphere/GeoSphere.
- A **map-mode shader** samples the ID texture, then indexes a small **palette/lookup texture** (provinceID → color) to produce political/terrain/etc. modes. Recoloring per tick = update the tiny palette texture only.
- Picking: mouse → ray-sphere hit → lat/lon → UV → sample ID texture (CPU-side copy) → province ID.
- Pros: trivial recolor, cheap, no runtime triangulation, matches how Paradox games actually work (the HOI4 map-modding wiki documents `provinces.bmp` + `definition.csv` ID mapping, plus `colormap` textures). Cons: texture resolution limits border crispness (mitigate with a border/edge-detection pass in the shader or a signed-distance approach).

**Approach B — Actual polygon triangulation on the sphere.**
- Triangulate each GeoJSON polygon and project vertices to the sphere.
- **Triangulation libraries (C#):** **LibTessDotNet** (https://github.com/speps/LibTessDotNet — robust GLU tessellator, handles holes/winding, best for messy GIS data), **Earcut ports** (MadWorldNL/EarCut, oberbichler/earcut.net — fast, handles holes, has a `flatten` for GeoJSON), **Triangle.NET**, **Poly2Tri**. For real-world country borders with holes and slivers, LibTessDotNet is the safe default; Earcut is faster if your data is clean.
- **Projection:** lat/lon (radians) → unit sphere: `x = cos(lat)·cos(lon)`, `y = sin(lat)`, `z = cos(lat)·sin(lon)` (adjust axis for Y-up). Scale by radius.
- **Antimeridian (±180° longitude):** polygons crossing the dateline will smear across the globe. Split them at ±180° before triangulating (clip into two polygons), or triangulate in 2D lon/lat then subdivide long edges so the interpolation follows the sphere.
- **Poles / large triangles:** a triangle spanning many degrees will cut *through* the sphere (chord vs arc). **Subdivide edges** so each triangle is small enough to hug the surface. Near the poles, lat/lon grids pinch — subdivided icosphere/GeoSphere topology avoids the singularity for the base sphere; for polygon fill, edge subdivision handles it.
- Pros: crisp borders, true geometry, per-province meshes possible. Cons: much more work; recolor still best done via vertex color / material params rather than re-triangulating.

**Recommendation:** start with **Approach A** (ID texture + map-mode shader). It's how the genre does it, gives you all map modes cheaply, and defers the hard triangulation work. Add Approach B later only if you need vector-crisp borders or extruded/3D provinces. You can also do a **hybrid**: triangulated meshes for land outlines/borders, ID texture for fill coloring.

**Open-source references worth reading for technique (any engine):**
- **HOI4 Map modding wiki** (https://hoi4.paradoxwikis.com/Map_modding) — authoritative on the province-bitmap + definition.csv + colormap approach.
- **Red Blob Games – Delaunay+Voronoi on a sphere** (https://www.redblobgames.com/x/1842-delaunay-voronoi-sphere/) — sphere meshing, the stereographic-projection trick for reusing 2D Delaunay libraries, pole handling.
- **globe.gl / three-globe** and **globe-ar** (https://github.com/hermionewy/globe-ar) — render GeoJSON `Polygon`/`MultiPolygon` on a sphere; good reference for polygon-on-globe rendering.
- **Geo Triangulate** (https://github.com/jessihamel/geo_triangulate) — converts GeoJSON to sphere-ready triangles.
- **Unity Coordinate Mapper** (https://github.com/unitycoder/Unity_Coordinate_Mapping) — spherical UV mapping and lat/lon placement.
- **Godot "Province Map Builder"** (https://godotengine.org/asset-library/asset/4973) — a grand-strategy province map plugin (2D/image-based, but the province-region/metadata workflow is instructive).
- **World Map Globe Edition** (Unity asset) — includes a lat/lon↔sphere-position "Calculator" and frontier/highlight coloring; commercial but documented technique.

### 6. Honest warnings and engine-fit reality check

- **Where Stride docs/examples are thin:** custom render features / `RootRenderFeature`, advanced SDSL, compute shaders for mesh/texture generation (discussion #1309 shows a user struggling with exactly your kind of map/terrain use case), and anything about rendering a data-driven map. Expect to read engine source and ask on Discord.
- **UI:** the built-in UI has known bugs and perf issues, and at least one shipped strategy team (Distant Worlds 2) abandoned it for a custom SpriteBatch layer; budget time to evaluate Myra/ImGui/custom early.
- **Headless & networking:** no official headless mode; networking samples are POCs. You'll build the client/host bridge yourself (which is fine given your architecture).
- **Would another engine be lower-friction?** For a *pure thin renderer to an external sim*, honestly yes in some respects:
  - **Godot 4 with C#** has a gentler learning curve, better docs, and first-class 2D/3D — but its C# support, while good, is a second-class citizen vs GDScript and has its own marshalling quirks.
  - **Veldrid / Silk.NET** (custom renderer) gives you total control and is a natural fit for "just draw what the sim says," with no engine impedance — but you build *everything* (camera, picking, UI, asset loading) yourself.
  - **MonoGame** is mature and simple but 3D-bare-bones (you'd be close to a custom renderer anyway).
  - **Unity** has the richest ecosystem and the most globe/province assets, but it's not open-source, not pure .NET-idiomatic, and heavier.
  - **Stride's advantage for you:** it's fully C#/.NET (matches your stack and deterministic-logic sensibilities), MIT open-source, has a real ECS and editor when you want it, and can run code-only as a library. That's a coherent choice for a C#-centric team. You've evaluated it as viable; nothing here should reverse that — just go in with eyes open about the thin spots.

## Recommendations

1. **This week:** Do Phase 0 (3D math primer) + C# Beginner tutorial. Stand up a code-only Toolkit project. Prototype lat/lon→sphere with debug spheres. *Success benchmark: you can place a marker at the correct city on a rotating globe.*
2. **Weeks 2-4:** Build a base GeoSphere, wrap an **equirectangular province-ID texture**, and write a **map-mode SDSL shader** with palette lookup. Implement **ray-sphere picking → province ID**. *Benchmark: click a province, see its ID, recolor it by writing one pixel to the palette texture.*
3. **Weeks 4-8:** Wire the **ConcurrentQueue → SyncScript.Update()** bridge to your host; drive per-tick recoloring from real simulation state. Add an **ImGui debug HUD** now; plan the shipping HUD (Myra, Stride UI, or a custom SpriteBatch layer) later. *Benchmark: the globe recolors correctly every tick from host-pushed ownership data.*
4. **Later / only if needed:** Move to **Approach B triangulation** (LibTessDotNet) for crisp vector borders; add GPU instancing only for repeated markers (units, icons), not provinces.
5. **Thresholds that would change the plan:** If you hit sustained frame drops from texture updates, batch palette writes per N ticks. If border crispness from the ID texture is unacceptable, add an SDSL edge-detection border pass before committing to full triangulation. If Stride's UI or headless gaps block you for more than ~2 weeks, seriously prototype the same client in Godot-C# as a comparison before sinking more time.

## Caveats
- Some tutorial *videos* predate the Xenko→Stride rename; the code and APIs referenced are current for 4.x. Verify the FPS/Top-Down RPG templates actually build on 4.3 before depending on them (they had a shader bug historically).
- The "25,000 entities at 60 FPS" figure is a community benchmark on specific hardware (RTX 3060), not an official guarantee — treat as directional. Your province map should not rely on 4,500 separate entities anyway.
- Community networking projects (ClientServerSample, Networking.Simple, ET-Stride) are small, lightly-maintained proofs of concept (single-digit to ~28 commits) — reference them for patterns, don't build on them as frameworks.
- "No official headless mode" is accurate as of Stride 4.3; this may change, but plan around it.
- The globe-rendering technique recommendations synthesize how Paradox games and web globe libraries work; there is no single canonical Stride tutorial for this, so expect iteration.
- I could not confirm any grand-strategy title shipped on Stride using a *province globe*; Distant Worlds 2 (Code Force / Slitherine, 2022) is cited by Stride as a commercial strategy game built on the engine, but it is a space 4X, not a HOI4-style province map.