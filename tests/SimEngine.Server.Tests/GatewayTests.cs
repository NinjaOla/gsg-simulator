using SimEngine.Client;
using SimEngine.Contracts;
using SimEngine.Contracts.Wire;
using SimEngine.Game.Worlds;
using SimEngine.Networking;
using Xunit;

namespace SimEngine.Server.Tests;

/// <summary>
/// The client gateway over a transport: handshake, identity bound
/// to the connection, authorization, save-name sanitizing, streaming with
/// end-to-end acks, and session end on host leave/disconnect. Runs once per transport.
/// </summary>
public abstract class GatewayTests : IAsyncLifetime
{
    private const string WorldId = "grid4";
    private static readonly DateTimeOffset StartDate = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(20);

    private ServerTestHarness _harness = null!;
    private string _contentHash = null!;

    public async ValueTask InitializeAsync()
    {
        _harness = await StartHarnessAsync();
        _contentHash = StaticWorld.Load(WorldId).ContentHash;
    }

    public async ValueTask DisposeAsync()
    {
        await _harness.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected abstract Task<ServerTestHarness> StartHarnessAsync();

    private async Task<(GameClient Client, SessionClient Session)> HostGameAsync(GameSettings? settings = null)
    {
        var host = await _harness.ConnectAsync("Host", Ct);
        var listing = await host.CreateGameAsync(new CreateGameRequest { Name = "test", WorldId = WorldId, StartDate = StartDate, Seed = 42 }, Ct);
        var session = await host.JoinGameAsync(listing.SessionId, _contentHash, Ct);
        await session.UpdateSettingsAsync(settings ?? new GameSettings { AllowDebugCommands = true }, Ct);
        return (host, session);
    }

    [Fact]
    public async Task Handshake_AssignsPlayerIdFromSanitizedName()
    {
        var client = await _harness.ConnectAsync("Al ice/../", Ct);

        Assert.StartsWith("Alice-", client.PlayerId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestBeforeHello_IsRejectedAndDisconnected()
    {
        var raw = new RawClient(await _harness.ConnectRawAsync(Ct));

        var id = raw.Send(id => new ClientMessages.ListWorlds(id));

        var error = Assert.IsType<ServerMessages.ErrorReply>(await raw.ReceiveAsync());
        Assert.Equal(id, error.RequestId);
        Assert.Equal(WireErrorCode.HandshakeRequired, error.Code);
        Assert.IsType<ServerMessages.DisconnectingPush>(await raw.ReceiveAsync());
        await raw.AssertClosedAsync();
    }

    [Fact]
    public async Task Hello_WithWrongProtocolVersion_IsRejected()
    {
        var raw = new RawClient(await _harness.ConnectRawAsync(Ct));

        raw.Send(id => new ClientMessages.Hello(id, WireProtocol.Version + 1, "old"));

        var error = Assert.IsType<ServerMessages.ErrorReply>(await raw.ReceiveAsync());
        Assert.Equal(WireErrorCode.ProtocolVersionMismatch, error.Code);
        Assert.IsType<ServerMessages.DisconnectingPush>(await raw.ReceiveAsync());
        await raw.AssertClosedAsync();
    }

    [Fact]
    public async Task MalformedMessage_Disconnects()
    {
        var raw = new RawClient(await _harness.ConnectRawAsync(Ct));

        raw.Connection.TrySend("{\"$type\":\"shutdownServer\"}"u8.ToArray());

        Assert.IsType<ServerMessages.DisconnectingPush>(await raw.ReceiveAsync());
        await raw.AssertClosedAsync();
    }

    [Fact]
    public async Task SessionRequest_ForSessionNotJoined_IsRejected()
    {
        var (_, session) = await HostGameAsync();
        var raw = new RawClient(await _harness.ConnectRawAsync(Ct));
        raw.Send(id => new ClientMessages.Hello(id, WireProtocol.Version, "intruder"));
        Assert.IsType<ServerMessages.WelcomeReply>(await raw.ReceiveAsync());

        raw.Send(id => new ClientMessages.StartGame(id, session.SessionId, Paused: false));

        var error = Assert.IsType<ServerMessages.ErrorReply>(await raw.ReceiveAsync());
        Assert.Equal(WireErrorCode.NotJoined, error.Code);
        Assert.Equal(SessionStage.Lobby, (await session.GetStatusAsync(Ct)).Stage);
    }

    [Fact]
    public async Task Join_UnknownSession_IsRejected()
    {
        var client = await _harness.ConnectAsync("guest", Ct);

        var error = await Assert.ThrowsAsync<RemoteRequestException>(() => client.JoinGameAsync("no-such-game", _contentHash, Ct));
        Assert.Equal(WireErrorCode.UnknownSession, error.Code);
    }

    [Fact]
    public async Task Join_WithMismatchedContent_ReportsBothHashes()
    {
        var (_, session) = await HostGameAsync();
        var guest = await _harness.ConnectAsync("guest", Ct);

        var error = await Assert.ThrowsAsync<ContentMismatchException>(() => guest.JoinGameAsync(session.SessionId, "other-hash", Ct));

        Assert.Equal(_contentHash, error.ExpectedHash);
        Assert.Equal("other-hash", error.ActualHash);
    }

    [Fact]
    public async Task NonHost_CannotStartOrChangeSettings()
    {
        var (_, hostSession) = await HostGameAsync();
        var guest = await _harness.ConnectAsync("guest", Ct);
        var guestSession = await guest.JoinGameAsync(hostSession.SessionId, _contentHash, Ct);

        await Assert.ThrowsAsync<CommandRejectedException>(() => guestSession.StartGameAsync(ct: Ct));
        await Assert.ThrowsAsync<CommandRejectedException>(() => guestSession.UpdateSettingsAsync(new GameSettings(), Ct));
        Assert.Equal(hostSession.PlayerId, (await guestSession.GetStatusAsync(Ct)).HostPlayerId);
    }

    [Fact]
    public async Task TwoClients_SeeTheSameTicks()
    {
        var (_, hostSession) = await HostGameAsync();
        var guest = await _harness.ConnectAsync("guest", Ct);
        var guestSession = await guest.JoinGameAsync(hostSession.SessionId, _contentHash, Ct);
        await hostSession.StartGameAsync(paused: true, Ct);
        await using var hostView = await hostSession.SubscribeAsync(Ct);
        await using var guestView = await guestSession.SubscribeAsync(Ct);

        // 40 ticks crosses a month boundary, so treasuries change.
        var result = await hostSession.DebugStepAsync(40, Ct);

        Assert.True(hostView.Cache.WaitForTick(result.TickNumber, WaitTimeout));
        Assert.True(guestView.Cache.WaitForTick(result.TickNumber, WaitTimeout));
        Assert.Equal(result.CurrentDate, guestView.Cache.CurrentDate);
        Assert.True(guestView.Cache.TryGetCountry("ALP", out var guestAlpha));
        Assert.True(hostView.Cache.TryGetCountry("ALP", out var hostAlpha));
        Assert.True(guestAlpha.FundsE2 > 0);
        Assert.Equal(hostAlpha.FundsE2, guestAlpha.FundsE2);
    }

    [Fact]
    public async Task PauseControl_IsEnforcedForNonHost()
    {
        var (_, hostSession) = await HostGameAsync(new GameSettings { PauseControl = ControlAccess.HostOnly, InitialSpeed = 1 });
        var guest = await _harness.ConnectAsync("guest", Ct);
        var guestSession = await guest.JoinGameAsync(hostSession.SessionId, _contentHash, Ct);
        await hostSession.StartGameAsync(ct: Ct);

        await Assert.ThrowsAsync<CommandRejectedException>(() => guestSession.SendCommandAsync(new PausePlayerCommand(), Ct));
        await hostSession.SendCommandAsync(new PausePlayerCommand(), Ct);

        Assert.Equal(SessionStage.Paused, (await guestSession.GetStatusAsync(Ct)).Stage);
    }

    [Fact]
    public async Task Save_AcceptsPlainNames_AndRejectsPathsAndNonHosts()
    {
        var (_, hostSession) = await HostGameAsync();
        var guest = await _harness.ConnectAsync("guest", Ct);
        var guestSession = await guest.JoinGameAsync(hostSession.SessionId, _contentHash, Ct);

        Assert.Equal("first.json", await hostSession.SaveAsync("first", Ct));
        Assert.True(File.Exists(Path.Combine(_harness.SaveDirectory, "first.json")));

        await Assert.ThrowsAsync<ArgumentException>(() => hostSession.SaveAsync(@"..\escape", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => hostSession.SaveAsync(Path.Combine(Path.GetTempPath(), "abs.json"), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => hostSession.SaveAsync("CON", Ct));
        await Assert.ThrowsAsync<CommandRejectedException>(() => guestSession.SaveAsync("guest", Ct));
    }

    [Fact]
    public async Task Load_CreatesLobbyGameFromSave()
    {
        var settings = new GameSettings
        {
            PauseControl = ControlAccess.Anyone,
            SpeedControl = ControlAccess.Anyone,
            AllowDebugCommands = true,
            InitialSpeed = 5,
        };
        var (_, hostSession) = await HostGameAsync(settings);
        await hostSession.StartGameAsync(paused: true, Ct);
        await hostSession.DebugStepAsync(5, Ct);
        await hostSession.SaveAsync("loadme", Ct);
        var other = await _harness.ConnectAsync("other", Ct);

        var listing = await other.LoadGameAsync("loaded", "loadme", Ct);
        var session = await other.JoinGameAsync(listing.SessionId, _contentHash, Ct);

        var status = await session.GetStatusAsync(Ct);
        Assert.Equal(SessionStage.Lobby, status.Stage);
        Assert.Equal(5, status.TickNumber);
        Assert.Equal(other.PlayerId, status.HostPlayerId);
        Assert.Equal(settings, status.Settings);
        Assert.Equal(settings.InitialSpeed, status.Speed);

        var guest = await _harness.ConnectAsync("guest", Ct);
        await guest.JoinGameAsync(listing.SessionId, _contentHash, Ct);
        await session.StartGameAsync(paused: true, Ct);
        Assert.Equal(6, (await session.DebugStepAsync(1, Ct)).TickNumber);
    }

    [Fact]
    public async Task HostLeaving_EndsSessionForEveryone()
    {
        var (_, hostSession) = await HostGameAsync();
        var guest = await _harness.ConnectAsync("guest", Ct);
        var guestSession = await guest.JoinGameAsync(hostSession.SessionId, _contentHash, Ct);
        await using var guestView = await guestSession.SubscribeAsync(Ct);

        await hostSession.LeaveAsync(Ct);

        Assert.True(guestView.Cache.WaitForChange(guestView.Cache.Sequence, WaitTimeout));
        Assert.Equal(SessionStage.Ended, guestView.Cache.Stage);
        Assert.NotNull(guestView.Cache.EndReason);
        Assert.DoesNotContain(await guest.ListGamesAsync(Ct), g => g.SessionId == hostSession.SessionId);
    }

    [Fact]
    public async Task HostDisconnecting_EndsSession()
    {
        var (host, hostSession) = await HostGameAsync();
        var guest = await _harness.ConnectAsync("guest", Ct);
        var guestSession = await guest.JoinGameAsync(hostSession.SessionId, _contentHash, Ct);
        await using var guestView = await guestSession.SubscribeAsync(Ct);

        await host.DisposeAsync();

        Assert.True(guestView.Cache.WaitForChange(guestView.Cache.Sequence, WaitTimeout));
        Assert.Equal(SessionStage.Ended, guestView.Cache.Stage);
    }

    [Fact]
    public async Task GuestDisconnecting_InLobby_RemovesPlayer()
    {
        var (_, hostSession) = await HostGameAsync();
        var guest = await _harness.ConnectAsync("guest", Ct);
        await guest.JoinGameAsync(hostSession.SessionId, _contentHash, Ct);
        Assert.Equal(2, (await hostSession.GetStatusAsync(Ct)).Players.Length);

        await guest.DisposeAsync();

        var deadline = DateTime.UtcNow + WaitTimeout;
        while ((await hostSession.GetStatusAsync(Ct)).Players.Length != 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, Ct);
        }

        Assert.Equal([hostSession.PlayerId], (await hostSession.GetStatusAsync(Ct)).Players.Select(p => p.Id));
    }

    [Fact]
    public async Task Lobby_PushesPlayerNamesAndSettingsToSubscribers()
    {
        var (_, hostSession) = await HostGameAsync();
        await using var hostView = await hostSession.SubscribeAsync(Ct);
        var guest = await _harness.ConnectAsync("Guest Player", Ct);

        await guest.JoinGameAsync(hostSession.SessionId, _contentHash, Ct);

        await WaitUntilAsync(() => hostView.Cache.Players.Count == 2);
        Assert.Contains(hostView.Cache.Players, p => p.Id == guest.PlayerId && p.Name == "Guest Player" && p.Connected);

        await hostSession.UpdateSettingsAsync(new GameSettings { InitialSpeed = 4 }, Ct);
        await WaitUntilAsync(() => hostView.Cache.Settings.InitialSpeed == 4);
    }

    [Fact]
    public async Task GuestDisconnecting_AfterStart_StaysMemberMarkedDisconnected()
    {
        var (_, hostSession) = await HostGameAsync();
        var guest = await _harness.ConnectAsync("guest", Ct);
        await guest.JoinGameAsync(hostSession.SessionId, _contentHash, Ct);
        await hostSession.StartGameAsync(paused: true, Ct);
        await using var hostView = await hostSession.SubscribeAsync(Ct);

        await guest.DisposeAsync();

        await WaitUntilAsync(() => hostView.Cache.Players.Any(p => p.Id == guest.PlayerId && !p.Connected));
        Assert.Equal(SessionStage.Paused, hostView.Cache.Stage);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition.");
            await Task.Delay(20, Ct);
        }
    }

    [Fact]
    public async Task ClientThatStopsAcking_GetsMergedCatchUpAfterAck()
    {
        const int maxUnacked = 10; // SessionRuntimeOptions.Default
        var (_, hostSession) = await HostGameAsync();
        var raw = new RawClient(await _harness.ConnectRawAsync(Ct));
        raw.Send(id => new ClientMessages.Hello(id, WireProtocol.Version, "lagger"));
        Assert.IsType<ServerMessages.WelcomeReply>(await raw.ReceiveAsync());
        raw.Send(id => new ClientMessages.JoinGame(id, hostSession.SessionId, _contentHash));
        Assert.IsType<ServerMessages.OkReply>(await raw.ReceiveAsync());
        await hostSession.StartGameAsync(paused: true, Ct);
        raw.Send(id => new ClientMessages.Subscribe(id, hostSession.SessionId));
        await raw.ReceiveUntilAsync<ServerMessages.SnapshotReply>();

        await hostSession.DebugStepAsync(30, Ct);

        var updates = new List<SessionStreamUpdate>();
        for (var i = 0; i < maxUnacked; i++)
        {
            updates.Add(Assert.IsType<ServerMessages.StreamUpdatePush>(await raw.ReceiveAsync()).Update);
        }

        await Task.Delay(200, Ct);
        Assert.False(raw.Connection.Incoming.TryPeek(out _));

        raw.Send(new ClientMessages.AckUpdate(hostSession.SessionId, updates[^1].Sequence));

        var merged = Assert.IsType<ServerMessages.StreamUpdatePush>(await raw.ReceiveAsync()).Update;
        Assert.Equal(30, merged.Tick.TickNumber);
        Assert.Equal(30 - maxUnacked, merged.Tick.TicksExecuted);
    }

    private sealed class RawClient(ITransportConnection connection)
    {
        private long _nextId;

        public ITransportConnection Connection { get; } = connection;

        public long Send(Func<long, ClientMessage> create)
        {
            var id = ++_nextId;
            Send(create(id));
            return id;
        }

        public void Send(ClientMessage message) => Assert.True(Connection.TrySend(WireCodec.Encode(message)));

        public async Task<ServerMessage> ReceiveAsync()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(WaitTimeout);
            var data = await Connection.Incoming.ReadAsync(timeout.Token);
            return WireCodec.DecodeServer(data.Span);
        }

        public async Task<T> ReceiveUntilAsync<T>()
            where T : ServerMessage
        {
            while (true)
            {
                if (await ReceiveAsync() is T match)
                {
                    return match;
                }
            }
        }

        public async Task AssertClosedAsync()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(WaitTimeout);
            Assert.False(await Connection.Incoming.WaitToReadAsync(timeout.Token));
        }
    }
}

/// <summary>Gateway scenarios over the in-process transport (single-player path).</summary>
public sealed class InProcGatewayTests : GatewayTests
{
    protected override Task<ServerTestHarness> StartHarnessAsync() => ServerTestHarness.StartAsync(Ct);
}

/// <summary>Gateway scenarios over TCP loopback (multiplayer path).</summary>
public sealed class TcpGatewayTests : GatewayTests
{
    protected override Task<ServerTestHarness> StartHarnessAsync() => ServerTestHarness.StartTcpAsync(Ct);
}
