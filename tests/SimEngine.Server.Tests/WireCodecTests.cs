using SimEngine.Contracts;
using SimEngine.Contracts.Wire;
using Xunit;

namespace SimEngine.Server.Tests;

public sealed class WireCodecTests
{
    [Fact]
    public void ClientMessages_RoundTrip_IncludingPolymorphicCommands()
    {
        ClientMessage[] messages =
        [
            new ClientMessages.Hello(1, WireProtocol.Version, "alice"),
            new ClientMessages.CreateGame(2, new CreateGameRequest { Name = "g", WorldId = "grid4", StartDate = DateTimeOffset.UnixEpoch, Seed = ulong.MaxValue }),
            new ClientMessages.SendCommands(3, "s", [new PausePlayerCommand(), new ResumePlayerCommand(), new SetSpeedPlayerCommand(4)]),
            new ClientMessages.UpdateSettings(4, "s", new GameSettings { PauseControl = ControlAccess.Anyone, InitialSpeed = 2 }),
            new ClientMessages.AckUpdate("s", 99),
        ];

        foreach (var message in messages)
        {
            var decoded = WireCodec.DecodeClient(WireCodec.Encode(message));
            Assert.Equal(message.GetType(), decoded.GetType());
        }

        var commands = Assert.IsType<ClientMessages.SendCommands>(WireCodec.DecodeClient(WireCodec.Encode(messages[2]))).Commands;
        Assert.Equal<PlayerCommand>([new PausePlayerCommand(), new ResumePlayerCommand(), new SetSpeedPlayerCommand(4)], commands);
        Assert.Equal(ulong.MaxValue, Assert.IsType<ClientMessages.CreateGame>(WireCodec.DecodeClient(WireCodec.Encode(messages[1]))).Request.Seed);
    }

    [Fact]
    public void ServerMessages_RoundTrip()
    {
        var snapshot = new SessionSnapshot
        {
            SessionId = "s",
            WorldId = "grid4",
            CurrentDate = new DateTimeOffset(1836, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Countries = [new CountryState { Tag = "ALP", DisplayName = "Alpha", FundsE2 = 12345, ProvinceCount = 2 }],
            Stage = SessionStage.Paused,
            Players = [new PlayerInfo("a", "Alice", Connected: true), new PlayerInfo("b", "Bob", Connected: false)],
            HostPlayerId = "a",
        };
        var update = new SessionStreamUpdate
        {
            SessionId = "s",
            Sequence = 7,
            Tick = new TickResult { TickNumber = 3, TicksExecuted = 1 },
            HostPlayerId = "a",
            Settings = new GameSettings { InitialSpeed = 4 },
            Players = [new PlayerInfo("a", "Alice", Connected: true)],
            Events = ["e"],
            CountryDeltas = [new CountryTreasuryDelta { Tag = "ALP", FundsE2 = 1 }],
        };

        var decodedSnapshot = Assert.IsType<ServerMessages.SnapshotReply>(
            WireCodec.DecodeServer(WireCodec.Encode(new ServerMessages.SnapshotReply(1, snapshot)))).Snapshot;
        var decodedUpdate = Assert.IsType<ServerMessages.StreamUpdatePush>(
            WireCodec.DecodeServer(WireCodec.Encode(new ServerMessages.StreamUpdatePush(update)))).Update;
        var decodedGame = Assert.IsType<ServerMessages.GameReply>(
            WireCodec.DecodeServer(WireCodec.Encode(new ServerMessages.GameReply(2, null))));

        Assert.Equal(snapshot.CurrentDate, decodedSnapshot.CurrentDate);
        Assert.Equal(snapshot.Countries, decodedSnapshot.Countries);
        Assert.Equal(snapshot.Players, decodedSnapshot.Players);
        Assert.Equal(SessionStage.Paused, decodedSnapshot.Stage);
        Assert.Equal(update.Tick, decodedUpdate.Tick);
        Assert.Equal(update.CountryDeltas, decodedUpdate.CountryDeltas);
        Assert.Equal(update.Players, decodedUpdate.Players);
        Assert.Equal(update.Settings, decodedUpdate.Settings);
        Assert.Equal("a", decodedUpdate.HostPlayerId);
        Assert.Null(decodedGame.Game);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"$type\":\"unknown\"}")]
    [InlineData("{\"$type\":\"hello\",\"requestId\":1,\"protocolVersion\":1}")]
    [InlineData("{\"$type\":\"hello\",\"requestId\":1,\"protocolVersion\":1,\"playerName\":null}")]
    public void Decode_RejectsMalformedOrUnknownMessages(string json)
    {
        Assert.Throws<WireFormatException>(() => WireCodec.DecodeClient(System.Text.Encoding.UTF8.GetBytes(json)));
    }
}
