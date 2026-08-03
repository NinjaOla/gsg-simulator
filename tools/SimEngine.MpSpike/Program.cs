using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Streams;
using SimEngine;
using SimEngine.Contracts;
using SimEngine.Game;
using SimEngine.Game.Seeding;
using SimEngine.MpSpike;
using SimEngine.Server;
using SimEngine.Server.Worlds;
using SimEngine.State.Loading;
using SimEngine.State.Loading.GeoJson;

// Orleans go/no-go spike. Stands up a standalone silo, connects two external
// Orleans clients over the loopback TCP gateway sharing one session, and
// measures the cost the actor runtime adds over a direct engine, plus the
// streaming events-out path and save-snapshot size.
//
// Budgets (pass/fail printed at the end):
//   - in-process grain overhead vs direct engine:  < 1 ms
//   - external client step round-trip (loopback):  < 10 ms
//   - germany_admin1 save snapshot (raw):          < 1 MB

const string World = "germany_admin1";
var startDate = new DateTimeOffset(1836, 1, 1, 0, 0, 0, TimeSpan.Zero);
const ulong Seed = 1234;

const int Warmup = 50;
const int DirectIterations = 1000;
const int CoHostedIterations = 1000;
const int ExternalIterations = 300;
const int StreamIterations = 100;

Console.WriteLine("=== SimEngine MP runtime spike ===");
Console.WriteLine($"World: {World}   seed: {Seed}   start: {startDate:yyyy-MM-dd}");
Console.WriteLine();

// ---------------------------------------------------------------------------
// Standalone silo: localhost clustering (silo 11111 / gateway 30000),
// in-memory streams + pub-sub storage, plus the engine provider.
// ---------------------------------------------------------------------------
var silo = new HostBuilder()
    .UseOrleans(b => b
        .UseLocalhostClustering()
        .AddMemoryStreams(SessionStreams.ProviderName)
        .AddMemoryGrainStorage("PubSubStore"))
    .ConfigureServices(s => s.AddSimEngineServer())
    .ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning))
    .Build();

await silo.StartAsync();
Console.WriteLine("Silo started (gateway :30000).");

IHost? clientA = null;
IHost? clientB = null;
try
{
    // -----------------------------------------------------------------------
    // 1) Direct engine baseline — no actor runtime in the path at all.
    // -----------------------------------------------------------------------
    var directStats = new LatencyStats("Direct engine.Step() (no Orleans)");
    {
        var engine = BuildEngine();
        for (var i = 0; i < Warmup; i++) engine.Step();
        for (var i = 0; i < DirectIterations; i++) directStats.Measure(() => engine.Step());
    }

    // -----------------------------------------------------------------------
    // 2) Co-hosted grain — grain call originating inside the silo process
    //    (no network), isolating the runtime/dispatch overhead.
    // -----------------------------------------------------------------------
    var coHostedStats = new LatencyStats("Co-hosted grain StepAsync(1) (in-proc)");
    {
        var factory = silo.Services.GetRequiredService<IGrainFactory>();
        var grain = factory.GetGrain<IGameSessionGrain>("spike-cohosted");
        await grain.InitializeAsync(World, startDate, Seed);
        for (var i = 0; i < Warmup; i++) await grain.StepAsync(1);
        for (var i = 0; i < CoHostedIterations; i++)
            await coHostedStats.MeasureAsync(() => grain.StepAsync(1));
    }

    // -----------------------------------------------------------------------
    // 3) External clients over the loopback gateway (real TCP sockets).
    // -----------------------------------------------------------------------
    clientA = await ConnectClientAsync();
    clientB = await ConnectClientAsync();
    Console.WriteLine("Two external clients connected over loopback gateway.");
    Console.WriteLine();

    const string sharedSession = "spike-external";
    var clusterA = clientA.Services.GetRequiredService<IClusterClient>();
    var clusterB = clientB.Services.GetRequiredService<IClusterClient>();

    var grainA = clusterA.GetGrain<IGameSessionGrain>(sharedSession);
    await grainA.InitializeAsync(World, startDate, Seed);

    var externalStats = new LatencyStats("External client StepAsync(1) round-trip");
    for (var i = 0; i < Warmup; i++) await grainA.StepAsync(1);
    for (var i = 0; i < ExternalIterations; i++)
        await externalStats.MeasureAsync(() => grainA.StepAsync(1));

    // -----------------------------------------------------------------------
    // 4) Stream delivery: client A drives, client B observes via the session
    //    stream. Pre-key by the expected tick to avoid a publish/await race.
    // -----------------------------------------------------------------------
    var streamStats = new LatencyStats("Stream delivery (A steps -> B observes)");
    var issuedAt = new ConcurrentDictionary<long, long>();
    var delivered = 0;
    var allDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    var streamB = clusterB
        .GetStreamProvider(SessionStreams.ProviderName)
        .GetStream<SessionStreamUpdate>(SessionStreams.For(sharedSession));

    var handle = await streamB.SubscribeAsync((update, _) =>
    {
        if (issuedAt.TryRemove(update.Tick.TickNumber, out var ts))
        {
            streamStats.Record(Stopwatch.GetElapsedTime(ts).TotalMilliseconds);
            if (Interlocked.Increment(ref delivered) >= StreamIterations)
                allDelivered.TrySetResult();
        }

        return Task.CompletedTask;
    });

    var baseline = await grainA.StepAsync(0);
    var nextTick = baseline.TickNumber + 1;
    for (var i = 0; i < StreamIterations; i++)
    {
        var expectedTick = nextTick + i;
        issuedAt[expectedTick] = Stopwatch.GetTimestamp();
        await grainA.StepAsync(1);
    }

    var streamCompleted = await Task.WhenAny(allDelivered.Task, Task.Delay(TimeSpan.FromSeconds(30)));
    await handle.UnsubscribeAsync();

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

    Console.WriteLine("--- Events-out path (Orleans Streams) ---");
    Console.WriteLine(streamStats.Report());
    Console.WriteLine(streamCompleted == allDelivered.Task
        ? $"All {StreamIterations} updates delivered to observer."
        : $"WARNING: only {delivered}/{StreamIterations} updates delivered before timeout.");
    Console.WriteLine();

    Console.WriteLine("--- Save snapshot size (germany_admin1) ---");
    Console.WriteLine($"  raw:  {rawBytes,10:N0} bytes  ({rawBytes / 1024.0,8:F1} KB)");
    Console.WriteLine($"  gzip: {gzipBytes,10:N0} bytes  ({gzipBytes / 1024.0,8:F1} KB)   ratio {(double)gzipBytes / rawBytes:P0}");
    Console.WriteLine();

    // -----------------------------------------------------------------------
    // Budget verdicts.
    // -----------------------------------------------------------------------
    var grainOverheadMs = coHostedStats.Mean - directStats.Mean;
    var externalRoundTripMs = externalStats.Mean;
    var rawMb = rawBytes / (1024.0 * 1024.0);

    Console.WriteLine("--- Budget verdicts ---");
    PrintVerdict("In-proc grain overhead < 1 ms", grainOverheadMs, 1.0, $"{grainOverheadMs:F3} ms");
    PrintVerdict("External round-trip   < 10 ms", externalRoundTripMs, 10.0, $"{externalRoundTripMs:F3} ms");
    PrintVerdict("Raw snapshot          < 1 MB", rawMb, 1.0, $"{rawMb:F3} MB");
}
finally
{
    if (clientA is not null) await clientA.StopAsync();
    if (clientB is not null) await clientB.StopAsync();
    clientA?.Dispose();
    clientB?.Dispose();
    await silo.StopAsync();
    silo.Dispose();
}

return;

// ---------------------------------------------------------------------------
// Helpers.
// ---------------------------------------------------------------------------
static async Task<IHost> ConnectClientAsync()
{
    var host = new HostBuilder()
        .UseOrleansClient(c => c
            .UseLocalhostClustering()
            .AddMemoryStreams(SessionStreams.ProviderName))
        .ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning))
        .Build();

    await host.StartAsync();
    return host;
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
