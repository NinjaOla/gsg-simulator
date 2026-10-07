using System.Diagnostics;
using Akka.Actor;
using SimEngine.Client;
using SimEngine.Contracts;
using SimEngine.Server.Direct;
using Xunit;

namespace SimEngine.Server.Tests;

/// <summary>
/// Session lifecycle (lobby → running ⇄ paused), settings enforcement, the
/// self-driven tick scheduler, and per-subscriber backlog delivery.
/// </summary>
public sealed class SessionLifecycleTests : IAsyncLifetime
{
    private const string WorldId = "grid4";
    private const string Host = "host";
    private const string Guest = "guest";
    private const int MaxUnacked = 3;
    private const int MaxHeld = 20;
    private static readonly DateTimeOffset StartDate = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly SessionRuntimeOptions FastOptions = new()
    {
        SpeedIntervals =
        [
            TimeSpan.FromMilliseconds(300),
            TimeSpan.FromMilliseconds(150),
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(20),
            TimeSpan.Zero,
        ],
        MaxUnackedUpdates = MaxUnacked,
        MaxHeldUpdates = MaxHeld,
    };

    private ServerTestHarness _harness = null!;

    public async ValueTask InitializeAsync()
    {
        _harness = await ServerTestHarness.StartAsync(FastOptions, Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _harness.DisposeAsync();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Initializes a session with <see cref="Host"/> and <see cref="Guest"/> joined, still in the lobby.</summary>
    private async Task<ActorSessionClient> NewLobbyAsync(string sessionId, GameSettings? settings = null)
    {
        var session = _harness.Client.GetSession(sessionId);
        var info = await session.InitializeAsync(WorldId, StartDate, seed: 42, Ct);
        await session.JoinAsync(Host, info.ContentHash, Ct);
        await session.JoinAsync(Guest, info.ContentHash, Ct);
        if (settings is not null)
        {
            await session.UpdateSettingsAsync(Host, settings, Ct);
        }

        return session;
    }

    [Fact]
    public async Task NewSession_IsInLobby_AndDoesNotTick()
    {
        var session = await NewLobbyAsync("lobby-idle");

        await Task.Delay(200, Ct);
        var status = await session.GetStatusAsync(Ct);

        Assert.Equal(SessionStage.Lobby, status.Stage);
        Assert.Equal(0, status.TickNumber);
        Assert.Equal(Host, status.HostPlayerId);
        Assert.Equal([Guest, Host], status.Players.Select(p => p.Id));
    }

    [Fact]
    public async Task StartGame_OnlyHostMayStart()
    {
        var session = await NewLobbyAsync("start-host-only");

        await Assert.ThrowsAsync<CommandRejectedException>(() => session.StartGameAsync(Guest, ct: Ct));
        await session.StartGameAsync(Host, paused: true, Ct);

        Assert.Equal(SessionStage.Paused, (await session.GetStatusAsync(Ct)).Stage);
    }

    [Fact]
    public async Task Join_AfterStart_IsRejected()
    {
        var session = await NewLobbyAsync("join-after-start");
        var snapshot = await session.GetSnapshotAsync(Ct);
        await session.StartGameAsync(Host, paused: true, Ct);

        await Assert.ThrowsAsync<CommandRejectedException>(
            () => session.JoinAsync("late", snapshot.ContentHash, Ct));
    }

    [Fact]
    public async Task UpdateSettings_HostOnly_AndLobbyOnly()
    {
        var session = await NewLobbyAsync("settings-rules");

        await Assert.ThrowsAsync<CommandRejectedException>(
            () => session.UpdateSettingsAsync(Guest, new GameSettings(), Ct));
        await Assert.ThrowsAsync<CommandRejectedException>(
            () => session.UpdateSettingsAsync(Host, new GameSettings { InitialSpeed = 9 }, Ct));

        await session.UpdateSettingsAsync(Host, new GameSettings { InitialSpeed = 2 }, Ct);
        await session.StartGameAsync(Host, paused: true, Ct);

        Assert.Equal(2, (await session.GetStatusAsync(Ct)).Speed);
        await Assert.ThrowsAsync<CommandRejectedException>(
            () => session.UpdateSettingsAsync(Host, new GameSettings(), Ct));
    }

    [Fact]
    public async Task Command_FromNonMember_IsRejected()
    {
        var session = await NewLobbyAsync("non-member");
        await session.StartGameAsync(Host, paused: true, Ct);

        await Assert.ThrowsAsync<CommandRejectedException>(
            () => session.SendCommandAsync("stranger", new ResumePlayerCommand(), Ct));
    }

    [Fact]
    public async Task RunningSession_TicksWithoutClientRequests()
    {
        var session = await NewLobbyAsync("auto-tick", new GameSettings { InitialSpeed = 5 });

        await session.StartGameAsync(Host, ct: Ct);

        await WaitUntilAsync(async () => (await session.GetStatusAsync(Ct)).TickNumber >= 10);
    }

    [Fact]
    public async Task Speed1_TicksNoFasterThanItsInterval()
    {
        var session = await NewLobbyAsync("speed-1", new GameSettings { InitialSpeed = 1 });

        var clock = Stopwatch.StartNew();
        await session.StartGameAsync(Host, ct: Ct);
        await Task.Delay(1000, Ct);
        var ticks = (await session.GetStatusAsync(Ct)).TickNumber;
        var elapsed = clock.Elapsed;

        // Tick k cannot start before k * 300 ms after the game started.
        Assert.InRange(ticks, 1, (long)(elapsed / FastOptions.GetInterval(1)));
    }

    [Fact]
    public async Task PauseAndResume_AtMaxSpeed_StopAndContinueTicking_AndReachSubscribers()
    {
        var session = await NewLobbyAsync("pause-resume", new GameSettings { InitialSpeed = 5 });
        var subscription = await _harness.SubscribeAsync(session, Ct);
        try
        {
            await session.StartGameAsync(Host, ct: Ct);
            await WaitUntilAsync(async () => (await session.GetStatusAsync(Ct)).TickNumber > 0);

            await session.SendCommandAsync(Host, new PausePlayerCommand(), Ct);
            var pausedAt = (await session.GetStatusAsync(Ct)).TickNumber;
            await Task.Delay(200, Ct);

            Assert.Equal(pausedAt, (await session.GetStatusAsync(Ct)).TickNumber);
            await WaitUntilAsync(() => Task.FromResult(subscription.Cache.Stage == SessionStage.Paused));

            await session.SendCommandAsync(Host, new ResumePlayerCommand(), Ct);
            await WaitUntilAsync(async () => (await session.GetStatusAsync(Ct)).TickNumber > pausedAt);
            await WaitUntilAsync(() => Task.FromResult(subscription.Cache.Stage == SessionStage.Running));
        }
        finally
        {
            await subscription.DisposeAsync();
        }
    }

    [Fact]
    public async Task PauseControl_HostOnly_RejectsGuest()
    {
        var session = await NewLobbyAsync("pause-host-only", new GameSettings { InitialSpeed = 1 });
        await session.StartGameAsync(Host, ct: Ct);

        await Assert.ThrowsAsync<CommandRejectedException>(
            () => session.SendCommandAsync(Guest, new PausePlayerCommand(), Ct));
        await session.SendCommandAsync(Host, new PausePlayerCommand(), Ct);

        Assert.Equal(SessionStage.Paused, (await session.GetStatusAsync(Ct)).Stage);
    }

    [Fact]
    public async Task PauseControl_Anyone_AcceptsGuest()
    {
        var session = await NewLobbyAsync(
            "pause-anyone",
            new GameSettings { InitialSpeed = 1, PauseControl = ControlAccess.Anyone });
        await session.StartGameAsync(Host, ct: Ct);

        await session.SendCommandAsync(Guest, new PausePlayerCommand(), Ct);

        Assert.Equal(SessionStage.Paused, (await session.GetStatusAsync(Ct)).Stage);
    }

    [Fact]
    public async Task Pause_InLobby_IsRejected()
    {
        var session = await NewLobbyAsync("pause-lobby");

        await Assert.ThrowsAsync<CommandRejectedException>(
            () => session.SendCommandAsync(Host, new PausePlayerCommand(), Ct));
    }

    [Fact]
    public async Task SetSpeed_OutOfRange_IsRejected_AndSpeedControlIsEnforced()
    {
        var session = await NewLobbyAsync("speed-rules");
        await session.StartGameAsync(Host, paused: true, Ct);

        await Assert.ThrowsAsync<CommandRejectedException>(
            () => session.SendCommandAsync(Host, new SetSpeedPlayerCommand(0), Ct));
        await Assert.ThrowsAsync<CommandRejectedException>(
            () => session.SendCommandAsync(Host, new SetSpeedPlayerCommand(6), Ct));
        await Assert.ThrowsAsync<CommandRejectedException>(
            () => session.SendCommandAsync(Guest, new SetSpeedPlayerCommand(4), Ct));

        await session.SendCommandAsync(Host, new SetSpeedPlayerCommand(4), Ct);

        Assert.Equal(4, (await session.GetStatusAsync(Ct)).Speed);
    }

    [Fact]
    public async Task SetSpeed_SpeedControlAnyone_AcceptsGuest()
    {
        var session = await NewLobbyAsync(
            "speed-anyone",
            new GameSettings { SpeedControl = ControlAccess.Anyone });
        await session.StartGameAsync(Host, paused: true, Ct);

        await session.SendCommandAsync(Guest, new SetSpeedPlayerCommand(1), Ct);

        Assert.Equal(1, (await session.GetStatusAsync(Ct)).Speed);
    }

    [Fact]
    public async Task DebugStep_RejectedWhenDisabled()
    {
        var session = await NewLobbyAsync("debug-disabled");
        await session.StartGameAsync(Host, paused: true, Ct);

        await Assert.ThrowsAsync<CommandRejectedException>(() => session.DebugStepAsync(Host, 1, Ct));
    }

    [Fact]
    public async Task DebugStep_RejectedWhileRunning_AcceptedWhilePaused()
    {
        var session = await NewLobbyAsync(
            "debug-running",
            new GameSettings { AllowDebugCommands = true, InitialSpeed = 1 });
        await session.StartGameAsync(Host, ct: Ct);

        await Assert.ThrowsAsync<CommandRejectedException>(() => session.DebugStepAsync(Host, 1, Ct));

        await session.SendCommandAsync(Host, new PausePlayerCommand(), Ct);
        var before = (await session.GetStatusAsync(Ct)).TickNumber;
        var result = await session.DebugStepAsync(Guest, 5, Ct);

        Assert.Equal(before + 5, result.TickNumber);
        Assert.Equal(5, result.TicksExecuted);
    }

    [Fact]
    public async Task AckingSubscriber_ReceivesOneUpdatePerTick_WhileLaggingSubscriberIsHeldBack()
    {
        var session = await NewLobbyAsync("per-tick", new GameSettings { InitialSpeed = 4 });
        var fast = new Recorder();
        var slow = new Recorder();
        var fastActor = _harness.System.ActorOf(Props.Create(() => new RecordingActor(fast, session.SessionId, true)));
        var slowActor = _harness.System.ActorOf(Props.Create(() => new RecordingActor(slow, session.SessionId, false)));
        await session.SubscribeAsync(fastActor, Ct);
        await session.SubscribeAsync(slowActor, Ct);

        await session.StartGameAsync(Host, ct: Ct);
        await WaitUntilAsync(async () => (await session.GetStatusAsync(Ct)).TickNumber >= 20);
        await session.SendCommandAsync(Host, new PausePlayerCommand(), Ct);
        var finalTick = (await session.GetStatusAsync(Ct)).TickNumber;

        await WaitUntilAsync(() => Task.FromResult(fast.LastUpdate?.Stage == SessionStage.Paused));
        var fastTicks = fast.Updates.Where(u => u.Tick.TicksExecuted > 0).ToArray();
        Assert.All(fastTicks, u => Assert.Equal(1, u.Tick.TicksExecuted));
        Assert.Equal(Enumerable.Range(1, (int)finalTick).Select(i => (long)i), fastTicks.Select(u => u.Tick.TickNumber));

        Assert.Equal(MaxUnacked, slow.Messages.Length);
    }

    [Fact]
    public async Task LaggingSubscriber_GetsMergedCatchUp_AndConverges()
    {
        var session = await NewDebugSessionAsync("merged");
        var recorder = new Recorder();
        var actor = _harness.System.ActorOf(Props.Create(() => new RecordingActor(recorder, session.SessionId, false)));
        await session.SubscribeAsync(actor, Ct);
        var baseline = await session.GetSnapshotAsync(Ct);

        // 3 sent + 12 held stays under the resync cap of 20; 40 ticks would cross it.
        await session.DebugStepAsync(Host, 15, Ct);
        await WaitUntilAsync(() => Task.FromResult(recorder.Messages.Length == MaxUnacked));
        await Task.Delay(100, Ct);
        Assert.Equal(MaxUnacked, recorder.Messages.Length);

        actor.Tell(RecordingActor.EnableAcks.Instance);
        await WaitUntilAsync(() => Task.FromResult(recorder.Messages.Length == MaxUnacked + 1));

        var merged = Assert.IsType<SessionStreamUpdate>(recorder.Messages[^1]);
        Assert.Equal(15, merged.Tick.TickNumber);
        Assert.Equal(15 - MaxUnacked, merged.Tick.TicksExecuted);

        await AssertConvergesAsync(session, baseline, recorder);
    }

    [Fact]
    public async Task SubscriberPastHardCap_IsResyncedWithSnapshot()
    {
        var session = await NewDebugSessionAsync("resync");
        var recorder = new Recorder();
        var actor = _harness.System.ActorOf(Props.Create(() => new RecordingActor(recorder, session.SessionId, false)));
        await session.SubscribeAsync(actor, Ct);
        var baseline = await session.GetSnapshotAsync(Ct);

        // 40 ticks crosses a month boundary, so treasury changes; 37 held > cap of 20.
        await session.DebugStepAsync(Host, 40, Ct);
        await WaitUntilAsync(() => Task.FromResult(recorder.Messages.Length == MaxUnacked));

        actor.Tell(RecordingActor.EnableAcks.Instance);
        await WaitUntilAsync(() => Task.FromResult(recorder.Messages.Length == MaxUnacked + 1));

        var resync = Assert.IsType<SessionSnapshot>(recorder.Messages[^1]);
        Assert.Equal(40, resync.TickNumber);

        await AssertConvergesAsync(session, baseline, recorder);
    }

    private async Task<ActorSessionClient> NewDebugSessionAsync(string sessionId)
    {
        var session = await NewLobbyAsync(sessionId, new GameSettings { AllowDebugCommands = true });
        await session.StartGameAsync(Host, paused: true, Ct);
        return session;
    }

    private static async Task AssertConvergesAsync(ActorSessionClient session, SessionSnapshot baseline, Recorder recorder)
    {
        var cache = new SessionStateCache(baseline);
        foreach (var message in recorder.Messages)
        {
            switch (message)
            {
                case SessionStreamUpdate update:
                    cache.Apply(update);
                    break;
                case SessionSnapshot snapshot:
                    cache.ApplySnapshot(snapshot);
                    break;
            }
        }

        var authoritative = await session.GetSnapshotAsync(Ct);
        Assert.Equal(authoritative.TickNumber, cache.TickNumber);
        Assert.Equal(authoritative.CurrentDate, cache.CurrentDate);
        Assert.Equal(
            authoritative.Countries.Select(c => (c.Tag, c.FundsE2)).OrderBy(c => c.Tag),
            cache.Countries.Select(c => (c.Tag, c.FundsE2)));
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Timed out waiting for condition.");
            }

            await Task.Delay(20, Ct);
        }
    }

    private sealed class Recorder
    {
        private readonly object _gate = new();
        private readonly List<object> _messages = [];

        public object[] Messages
        {
            get { lock (_gate) { return _messages.ToArray(); } }
        }

        public SessionStreamUpdate[] Updates => Messages.OfType<SessionStreamUpdate>().ToArray();

        public SessionStreamUpdate? LastUpdate
        {
            get
            {
                var updates = Updates;
                return updates.Length > 0 ? updates[^1] : null;
            }
        }

        public void Add(object message)
        {
            lock (_gate)
            {
                _messages.Add(message);
            }
        }
    }

    /// <summary>Records updates/snapshots; acks only when enabled to simulate a lagging client.</summary>
    private sealed class RecordingActor : ReceiveActor
    {
        private IActorRef? _session;
        private long _lastSequence;
        private bool _ackEnabled;

        public RecordingActor(Recorder recorder, string sessionId, bool ackEnabled)
        {
            _ackEnabled = ackEnabled;

            Receive<SessionStreamUpdate>(update => Record(recorder, sessionId, update, update.Sequence));
            Receive<SessionSnapshot>(snapshot => Record(recorder, sessionId, snapshot, snapshot.Sequence));
            Receive<EnableAcks>(_ =>
            {
                _ackEnabled = true;
                _session?.Tell(new SessionProtocol.AckUpdate(sessionId, _lastSequence));
            });
        }

        private void Record(Recorder recorder, string sessionId, object message, long sequence)
        {
            recorder.Add(message);
            _session = Sender;
            _lastSequence = sequence;
            if (_ackEnabled)
            {
                Sender.Tell(new SessionProtocol.AckUpdate(sessionId, sequence));
            }
        }

        public sealed class EnableAcks
        {
            public static readonly EnableAcks Instance = new();
        }
    }
}
