using System.Diagnostics;
using System.IO.Compression;
using Akka.Actor;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimEngine;
using SimEngine.Client;
using SimEngine.Contracts;
using SimEngine.Game;
using SimEngine.Game.Seeding;
using SimEngine.MpSpike;
using SimEngine.Networking;
using SimEngine.Networking.Tcp;
using SimEngine.Server;
using SimEngine.Server.Direct;
using SimEngine.Game.Worlds;
using SimEngine.State.Loading;
using SimEngine.State.Loading.GeoJson;

// Akka go/no-go spike. Stands up a standalone server actor system with a TCP
// gateway, connects an external wire client over loopback sharing one session,
// and measures the cost the actor runtime adds over a direct engine, plus the
// per-tick broadcast path and save-snapshot size.
//
// Budgets (pass/fail printed at the end):
//   - in-process actor overhead vs direct engine:  < 1 ms
//   - external client advance round-trip (loopback): < 10 ms
//   - germany_admin1 save snapshot (raw):          < 1 MB

const string World = "germany_admin1";
var startDate = new DateTimeOffset(1836, 1, 1, 0, 0, 0, TimeSpan.Zero);
const ulong Seed = 1234;

const int Warmup = 50;
const int DirectIterations = 1000;
const int CoHostedIterations = 1000;
const int ExternalIterations = 300;
const int StreamIterations = 100;

const string ServerSystemName = "SimEngineServer";
const string SpikePlayer = "spike";

Console.WriteLine("=== SimEngine MP runtime spike ===");
Console.WriteLine($"World: {World}   seed: {Seed}   start: {startDate:yyyy-MM-dd}");
Console.WriteLine();

// ---------------------------------------------------------------------------
// Standalone server actor system with a TCP gateway on an ephemeral loopback port.
// ---------------------------------------------------------------------------
var transports = new TransportRegistry(new TcpTransport());
var listener = await transports.ListenAsync(TcpTransport.CreateEndpoint("127.0.0.1", 0));
var serverEndpoint = listener.LocalEndpoint;
var server = new HostBuilder()
    .ConfigureServices(s =>
    {
        s.AddAkka(ServerSystemName, builder =>
        {
            builder
                .WithSimEngineActors(AkkaExecutionMode.LocalTest)
                .WithSimEngineGateway(listener);
        });
    })
    .ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning))
    .Build();

await server.StartAsync();
Console.WriteLine($"Server started ({serverEndpoint}).");

GameClient? client = null;
try
{
    var serverRegistry = server.Services.GetRequiredService<ActorRegistry>();
    var serverSystem = server.Services.GetRequiredService<ActorSystem>();

    // -----------------------------------------------------------------------
    // 1) Direct engine baseline - no actor runtime in the path at all.
    // -----------------------------------------------------------------------
    var directStats = new LatencyStats("Direct engine.Step() (no actors)");
    {
        var engine = BuildEngine();
        for (var i = 0; i < Warmup; i++) engine.Step();
        for (var i = 0; i < DirectIterations; i++) directStats.Measure(() => engine.Step());
    }

    // -----------------------------------------------------------------------
    // 2) Co-hosted actor - Ask originating inside the server process (no
    //    network), isolating the runtime/dispatch overhead.
    // -----------------------------------------------------------------------
    var coHostedStats = new LatencyStats("Co-hosted actor DebugStepAsync(1) (in-proc)");
    {
        var local = ActorGameClient.FromLocalRegistry(serverSystem, serverRegistry);
        var session = local.GetSession("spike-cohosted");
        await PrepareDebugSessionAsync(session);
        for (var i = 0; i < Warmup; i++) await session.DebugStepAsync(SpikePlayer, 1);
        for (var i = 0; i < CoHostedIterations; i++)
            await coHostedStats.MeasureAsync(() => session.DebugStepAsync(SpikePlayer, 1));
    }

    // -----------------------------------------------------------------------
    // 3) External wire client through the TCP gateway (real loopback sockets).
    // -----------------------------------------------------------------------
    client = await GameClient.ConnectAsync(transports, serverEndpoint, SpikePlayer);
    Console.WriteLine("External client connected over TCP.");
    Console.WriteLine();

    var listing = await client.CreateGameAsync(new CreateGameRequest { Name = "spike-external", WorldId = World, StartDate = startDate, Seed = Seed });
    var externalSession = await client.JoinGameAsync(listing.SessionId, StaticWorld.Load(World).ContentHash);
    await externalSession.UpdateSettingsAsync(new GameSettings { AllowDebugCommands = true });
    await externalSession.StartGameAsync(paused: true);

    var externalStats = new LatencyStats("External client DebugStepAsync(1) round-trip");
    for (var i = 0; i < Warmup; i++) await externalSession.DebugStepAsync(1);
    for (var i = 0; i < ExternalIterations; i++)
        await externalStats.MeasureAsync(() => externalSession.DebugStepAsync(1));

    // -----------------------------------------------------------------------
    // 4) Broadcast delivery: time from issuing an advance until the client's
    //    synced cache reflects the resulting tick (pushed over the wire).
    // -----------------------------------------------------------------------
    var streamStats = new LatencyStats("Broadcast delivery (advance -> observe)");
    var delivered = 0;
    await using (var subscription = await externalSession.SubscribeAsync())
    {
        for (var i = 0; i < StreamIterations; i++)
        {
            var issued = Stopwatch.GetTimestamp();
            var result = await externalSession.DebugStepAsync(1);
            if (subscription.Cache.WaitForTick(result.TickNumber, TimeSpan.FromSeconds(30)))
            {
                streamStats.Record(Stopwatch.GetElapsedTime(issued).TotalMilliseconds);
                delivered++;
            }
        }
    }
    var allDelivered = delivered == StreamIterations;

    // -----------------------------------------------------------------------
    // 5) Save-snapshot size for the real-world map.
    // -----------------------------------------------------------------------
    var (rawBytes, gzipBytes) = MeasureSnapshot();

    // -----------------------------------------------------------------------
    // Report.
    // -----------------------------------------------------------------------
    Console.WriteLine("--- Step latency (single-day ticks; isolates call overhead) ---");
    Console.WriteLine(directStats.Report());
    Console.WriteLine(coHostedStats.Report());
    Console.WriteLine(externalStats.Report());
    Console.WriteLine();

    Console.WriteLine("--- Events-out path (wire broadcast) ---");
    Console.WriteLine(streamStats.Report());
    Console.WriteLine(allDelivered
        ? $"All {StreamIterations} updates delivered to the client."
        : $"WARNING: only {delivered}/{StreamIterations} updates delivered before timeout.");
    Console.WriteLine();

    Console.WriteLine("--- Save snapshot size (germany_admin1) ---");
    Console.WriteLine($"  raw:  {rawBytes,10:N0} bytes  ({rawBytes / 1024.0,8:F1} KB)");
    Console.WriteLine($"  gzip: {gzipBytes,10:N0} bytes  ({gzipBytes / 1024.0,8:F1} KB)   ratio {(double)gzipBytes / rawBytes:P0}");
    Console.WriteLine();

    // -----------------------------------------------------------------------
    // Budget verdicts.
    // -----------------------------------------------------------------------
    var actorOverheadMs = coHostedStats.Mean - directStats.Mean;
    var externalRoundTripMs = externalStats.Mean;
    var rawMb = rawBytes / (1024.0 * 1024.0);

    Console.WriteLine("--- Budget verdicts ---");
    PrintVerdict("In-proc actor overhead < 1 ms", actorOverheadMs, 1.0, $"{actorOverheadMs:F3} ms");
    PrintVerdict("External round-trip   < 10 ms", externalRoundTripMs, 10.0, $"{externalRoundTripMs:F3} ms");
    PrintVerdict("Raw snapshot          < 1 MB", rawMb, 1.0, $"{rawMb:F3} MB");
}
finally
{
    if (client is not null)
    {
        await client.DisposeAsync();
    }

    await server.StopAsync();
    server.Dispose();
}

return;

// ---------------------------------------------------------------------------
// Helpers.
// ---------------------------------------------------------------------------

// Initializes, joins as host, enables debug commands, and starts paused so DebugStep is allowed.
async Task PrepareDebugSessionAsync(ActorSessionClient session)
{
    var info = await session.InitializeAsync(World, startDate, Seed);
    await session.JoinAsync(SpikePlayer, info.ContentHash);
    await session.UpdateSettingsAsync(SpikePlayer, new GameSettings { AllowDebugCommands = true });
    await session.StartGameAsync(SpikePlayer, paused: true);
}

SimulationEngine BuildEngine()
{
    var asset = WorldCatalog.Find(World)
        ?? throw new InvalidOperationException($"Unknown world '{World}'.");

    var worldPath = WorldCatalog.ResolvePath(asset);
    var countriesPath = WorldCatalog.ResolveCountriesPath(asset);
    using var worldStream = File.OpenRead(worldPath);
    var worldResult = new GeoJsonWorldLoader().Load(worldStream);
    var state = WorldLoaders.LoadIntoState(worldResult);
    GameWorldSeeder.Seed(state, worldResult, countriesPath);
    state.Metadata["worldName"] = asset.DisplayName;

    var definition = GameDefinition.CreateDefault(World, "dev", "dev");
    return new SimulationEngine(
        new SimulationEngineOptions
        {
            StartDate = startDate,
            Seed = Seed,
            InitialState = state,
            ComponentCodecs = definition.ComponentCodecs,
            StateSectionCodecs = definition.StateSectionCodecs,
            SaveMetadata = definition.SaveMetadata,
        },
        definition.Systems);
}

(long Raw, long Gzip) MeasureSnapshot()
{
    var engine = BuildEngine();
    engine.StepMany(120); // ~4 months so populations/treasury are non-trivial

    using var raw = new MemoryStream();
    engine.Save(raw);
    var rawBytes = raw.ToArray();

    using var compressed = new MemoryStream();
    using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
    {
        gzip.Write(rawBytes);
    }

    return (rawBytes.Length, compressed.Length);
}

static void PrintVerdict(string label, double value, double budget, string formatted)
{
    var pass = value < budget;
    var mark = pass ? "PASS" : "FAIL";
    Console.WriteLine($"  [{mark}] {label,-32} actual {formatted}");
}
