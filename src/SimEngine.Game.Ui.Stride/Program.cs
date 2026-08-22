using SimEngine.Game.Ui.Stride;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Games;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Input;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;
using System.Text.Json;

using var game = new Game();
var selection = GeoJsonSelection.Parse(args);

Entity? sphereEntity = null;
Entity? menuUiEntity = null;
Scene? activeScene = null;
Entity? activeCameraEntity = null;
bool inMainMenu = true;
float spinAngle = 0f;
const float SphereDiameter = 4.5f;

SpriteFont? menuFont = null;

game.Run(start: Start, update: Update);

void Start(Scene rootScene)
{
    game.SetupBase3D();

    activeScene = rootScene;
    var scene = activeScene;
    var cameraEntity = scene.Entities.FirstOrDefault(static entity => entity.Get<CameraComponent>() is not null);

    if (cameraEntity is null)
    {
        cameraEntity = new Entity("Camera")
        {
            new CameraComponent
            {
                NearClipPlane = 0.01f,
                FarClipPlane = 2000f,
            },
        };

        scene.Entities.Add(cameraEntity);

        cameraEntity.Transform.Position = new Vector3(0f, 0f, -10f);
        cameraEntity.Transform.Rotation = Quaternion.Identity;
    }

    activeCameraEntity = cameraEntity;

    try
    {
        menuFont = game.Content.Load<SpriteFont>("StrideDefaultFont");
    }
    catch
    {
        // Keep menu functional even if explicit font load fails.
    }

    var menuLayout = new StackPanel
    {
        Orientation = Orientation.Vertical,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    menuLayout.Children.Add(new TextBlock
    {
        Text = "GSG Simulator",
        TextColor = new Color(242, 247, 252),
        TextSize = 48f,
        Font = menuFont,
    });

    menuLayout.Children.Add(new TextBlock
    {
        Text = "Press Enter to Start",
        TextColor = new Color(198, 214, 226),
        TextSize = 24f,
        Font = menuFont,
    });

    menuLayout.Children.Add(new TextBlock
    {
        Text = "(or click the buttons)",
        TextColor = new Color(176, 196, 214),
        TextSize = 16f,
        Font = menuFont,
    });

    menuLayout.Children.Add(new TextBlock
    {
        Text = " ",
        TextSize = 10f,
    });

    var startButton = new Button
    {
        Content = new Border
        {
            Width = 360f,
            BackgroundColor = new Color(36, 122, 196, 255),
            BorderColor = new Color(202, 225, 242, 255),
            BorderThickness = new Thickness(2f, 2f, 2f, 2f),
            Padding = new Thickness(24f, 14f, 24f, 14f),
            HorizontalAlignment = HorizontalAlignment.Center,
            Content = new TextBlock
            {
                Text = "START GAME",
                TextColor = new Color(248, 251, 255),
                TextSize = 26f,
                TextAlignment = TextAlignment.Center,
                Font = menuFont,
            },
        },
        SizeToContent = true,
        ClickMode = ClickMode.Release,
    };
    startButton.Click += (_, _) => StartGameScene();
    menuLayout.Children.Add(startButton);

    menuLayout.Children.Add(new TextBlock
    {
        Text = " ",
        TextSize = 6f,
    });

    var exitButton = new Button
    {
        Content = new Border
        {
            Width = 360f,
            BackgroundColor = new Color(150, 54, 54, 255),
            BorderColor = new Color(242, 206, 206, 255),
            BorderThickness = new Thickness(2f, 2f, 2f, 2f),
            Padding = new Thickness(24f, 12f, 24f, 12f),
            HorizontalAlignment = HorizontalAlignment.Center,
            Content = new TextBlock
            {
                Text = "EXIT",
                TextColor = new Color(255, 245, 245),
                TextSize = 22f,
                TextAlignment = TextAlignment.Center,
                Font = menuFont,
            },
        },
        SizeToContent = true,
        ClickMode = ClickMode.Release,
    };
    exitButton.Click += (_, _) => game.Exit();
    menuLayout.Children.Add(exitButton);

    var menuRoot = new Grid
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
    };
    menuRoot.Children.Add(menuLayout);
    menuUiEntity = new Entity("MainMenuUI")
    {
        new UIComponent
        {
            IsFullScreen = true,
            Page = new UIPage { RootElement = menuRoot },
        },
    };
    scene.Entities.Add(menuUiEntity);

    var geoJsonPath = selection.ResolvePath();
    var featureCount = TryCountFeatures(geoJsonPath);
    global::System.Console.WriteLine($"Main menu loaded. GeoJSON ready in memory: {geoJsonPath} features={featureCount}");
    global::System.Console.WriteLine("Press Enter to start game scene.");
}

void Update(Scene _, GameTime gameTime)
{
    if (inMainMenu)
    {
        if (game.Input.IsKeyPressed(Keys.Enter) || game.Input.IsKeyPressed(Keys.S))
        {
            StartGameScene();
        }

        if (game.Input.IsKeyPressed(Keys.Escape))
        {
            game.Exit();
        }

        return;
    }

    if (sphereEntity is null)
    {
        return;
    }

    spinAngle += (float)gameTime.Elapsed.TotalSeconds * 0.6f;
    sphereEntity.Transform.Rotation = Quaternion.RotationAxis(Vector3.UnitY, spinAngle);
}

void StartGameScene()
{
    if (activeScene is null || !inMainMenu)
    {
        return;
    }

    inMainMenu = false;

    if (menuUiEntity is not null)
    {
        activeScene.Entities.Remove(menuUiEntity);
        menuUiEntity = null;
    }

    sphereEntity = game.Create3DPrimitive(
        PrimitiveModelType.Sphere,
        new Primitive3DEntityOptions
        {
            EntityName = "GameSphere",
            Material = game.CreateFlatMaterial(new Color(255, 128, 40)),
            Size = new Vector3(SphereDiameter),
        });

    sphereEntity.Transform.Position = Vector3.Zero;
    activeScene.Entities.Add(sphereEntity);

    // The toolkit maps Primitive3DEntityOptions.Size.X to SphereProceduralModel.Radius,
    // so the sphere's actual radius equals SphereDiameter (despite the name).
    var globeRadius = SphereDiameter;
    var geoJsonPath = selection.ResolvePath();

    var borderEntity = GeoJsonBorderLines.CreateEntity(
        game,
        geoJsonPath,
        globeRadius,
        borderColor: new Color(235, 245, 255),
        maxSegments: 2_000_000);
    borderEntity.Transform.Position = Vector3.Zero;
    activeScene.Entities.Add(borderEntity);

    var provinceIndex = GeoJsonProvinceIndex.Load(geoJsonPath);

    if (activeCameraEntity is not null)
    {
        activeCameraEntity.Add(new GlobeOrbitCameraController
        {
            Target = Vector3.Zero,
            Radius = 12f,
            MinRadius = globeRadius + 1f,
            MaxRadius = 30f,
            OrbitSensitivity = 0.012f,
            ZoomSensitivity = 0.75f,
        });

        if (activeCameraEntity.Get<CameraComponent>() is { } cameraComponent)
        {
            activeCameraEntity.Add(new GlobePickScript
            {
                Camera = cameraComponent,
                ProvinceIndex = provinceIndex,
                GlobeRadius = globeRadius,
            });
        }
    }

    global::System.Console.WriteLine(
        $"Game scene started. Borders drawn from {geoJsonPath} provinces={provinceIndex.FeatureCount}");
}

static int TryCountFeatures(string geoJsonPath)
{
    try
    {
        using var stream = File.OpenRead(geoJsonPath);
        using var doc = JsonDocument.Parse(stream);

        if (!doc.RootElement.TryGetProperty("features", out var features)
            || features.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return features.GetArrayLength();
    }
    catch
    {
        return 0;
    }
}

internal sealed class GeoJsonSelection
{
    private const string DefaultGeoJson = "grid4.geojson";
    private const string FullWorldGeoJson = "world_admin1.geojson";

    private readonly string? customPath;
    private readonly bool useFullWorld;

    private GeoJsonSelection(string? customPath, bool useFullWorld)
    {
        this.customPath = customPath;
        this.useFullWorld = useFullWorld;
    }

    public static GeoJsonSelection Parse(string[] args)
    {
        string? customPath = null;
        var useFullWorld = true;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Equals("--full-world", StringComparison.OrdinalIgnoreCase))
            {
                useFullWorld = true;
                continue;
            }

            if (arg.Equals("--grid", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--small", StringComparison.OrdinalIgnoreCase))
            {
                useFullWorld = false;
                continue;
            }

            if (arg.StartsWith("--geojson=", StringComparison.OrdinalIgnoreCase))
            {
                customPath = arg[10..].Trim('"');
                continue;
            }

            if (arg.Equals("--geojson", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                customPath = args[i + 1].Trim('"');
                i++;
            }
        }

        return new GeoJsonSelection(customPath, useFullWorld);
    }

    public string ResolvePath()
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            return ResolveCustomPath(customPath);
        }

        var fileName = useFullWorld ? FullWorldGeoJson : DefaultGeoJson;
        if (TryResolveExistingPath(
            [
                Path.Combine("Worlds", fileName),
                Path.Combine(AppContext.BaseDirectory, "Worlds", fileName),
                Path.Combine("data", "custom-test", fileName),
                Path.Combine("data", "full-world", "states", fileName),
                Path.Combine(AppContext.BaseDirectory, "data", "custom-test", fileName),
                Path.Combine(AppContext.BaseDirectory, "data", "full-world", "states", fileName),
            ],
            out var resolved))
        {
            return resolved;
        }

        throw new FileNotFoundException(
            $"Bundled GeoJSON not found for '{fileName}'. " +
            "Tried Worlds/ and data/ locations relative to both current directory and app base directory. " +
            "Use --geojson to pass an explicit file path.");
    }

    private static string ResolveCustomPath(string rawPath)
    {
        if (TryResolveExistingPath(
            [
                rawPath,
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), rawPath)),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rawPath)),
            ],
            out var resolved))
        {
            return resolved;
        }

        throw new FileNotFoundException(
            $"GeoJSON file not found: '{rawPath}'. " +
            "Checked raw path, current-directory relative, and app-base-directory relative forms.",
            rawPath);
    }

    private static bool TryResolveExistingPath(IEnumerable<string> candidates, out string resolved)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var normalized = Path.GetFullPath(candidate);
            if (File.Exists(normalized))
            {
                resolved = normalized;
                return true;
            }
        }

        resolved = string.Empty;
        return false;
    }
}








