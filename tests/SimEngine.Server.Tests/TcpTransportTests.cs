using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using SimEngine.Networking;
using SimEngine.Networking.Tcp;
using Xunit;

namespace SimEngine.Server.Tests;

/// <summary>TCP framing, heartbeats, endpoint parsing, and the parent-process watchdog.</summary>
public sealed class TcpTransportTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(20);
    private static readonly TransportEndpoint Loopback = TcpTransport.CreateEndpoint("127.0.0.1", 0);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Listener_OnPortZero_ReportsBoundPort()
    {
        await using var listener = await new TcpTransport().ListenAsync(Loopback, Ct);

        var (host, port) = TcpTransport.ParseAddress(listener.LocalEndpoint);

        Assert.Equal("127.0.0.1", host);
        Assert.NotEqual(0, port);
    }

    [Fact]
    public async Task LargeMessages_RoundTripInBothDirections()
    {
        var transport = new TcpTransport();
        await using var listener = await transport.ListenAsync(Loopback, Ct);
        await using var client = await transport.ConnectAsync(listener.LocalEndpoint, Ct);
        await using var server = await listener.AcceptAsync(Ct);
        var payload = new byte[5 * 1024 * 1024];
        System.Random.Shared.NextBytes(payload);

        Assert.True(client.TrySend(payload));
        Assert.True(server.TrySend(payload));

        Assert.Equal(payload, (await ReadAsync(server)).ToArray());
        Assert.Equal(payload, (await ReadAsync(client)).ToArray());
    }

    [Fact]
    public async Task FrameSplitAcrossReads_IsReassembled()
    {
        await using var listener = await new TcpTransport().ListenAsync(Loopback, Ct);
        using var socket = await ConnectRawSocketAsync(listener);
        await using var server = await listener.AcceptAsync(Ct);
        var frame = Frame("hello"u8.ToArray());

        foreach (var b in frame)
        {
            await socket.SendAsync(new[] { b }, SocketFlags.None, Ct);
            await Task.Delay(5, Ct);
        }

        Assert.Equal("hello"u8.ToArray(), (await ReadAsync(server)).ToArray());
    }

    [Fact]
    public async Task OversizedFrame_ClosesConnection()
    {
        await using var listener = await new TcpTransport().ListenAsync(Loopback, Ct);
        using var socket = await ConnectRawSocketAsync(listener);
        await using var server = await listener.AcceptAsync(Ct);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, TransportLimits.MaxMessageBytes + 1);

        await socket.SendAsync(header, SocketFlags.None, Ct);

        await server.Completion.WaitAsync(WaitTimeout, Ct);
    }

    [Fact]
    public async Task SilentPeer_IsClosedAfterIdleTimeout()
    {
        var options = new TcpTransportOptions { HeartbeatInterval = TimeSpan.FromMilliseconds(50), IdleTimeout = TimeSpan.FromMilliseconds(300) };
        await using var listener = await new TcpTransport(options).ListenAsync(Loopback, Ct);
        using var socket = await ConnectRawSocketAsync(listener);
        await using var server = await listener.AcceptAsync(Ct);

        await server.Completion.WaitAsync(WaitTimeout, Ct);
    }

    [Fact]
    public async Task Heartbeats_KeepIdleConnectionsOpen()
    {
        var transport = new TcpTransport(new TcpTransportOptions { HeartbeatInterval = TimeSpan.FromMilliseconds(50), IdleTimeout = TimeSpan.FromMilliseconds(300) });
        await using var listener = await transport.ListenAsync(Loopback, Ct);
        await using var client = await transport.ConnectAsync(listener.LocalEndpoint, Ct);
        await using var server = await listener.AcceptAsync(Ct);

        await Task.Delay(1000, Ct);

        Assert.False(client.Completion.IsCompleted);
        Assert.False(server.Completion.IsCompleted);
        Assert.True(client.TrySend("still here"u8.ToArray()));
        Assert.Equal("still here"u8.ToArray(), (await ReadAsync(server)).ToArray());
    }

    [Fact]
    public async Task Dispose_FlushesQueuedMessages_ThenClosesPeer()
    {
        var transport = new TcpTransport();
        await using var listener = await transport.ListenAsync(Loopback, Ct);
        var client = await transport.ConnectAsync(listener.LocalEndpoint, Ct);
        await using var server = await listener.AcceptAsync(Ct);

        Assert.True(client.TrySend("bye"u8.ToArray()));
        await client.DisposeAsync();

        Assert.Equal("bye"u8.ToArray(), (await ReadAsync(server)).ToArray());
        await server.Completion.WaitAsync(WaitTimeout, Ct);
        Assert.False(client.TrySend("late"u8.ToArray()));
    }

    [Fact]
    public async Task Connect_WithNothingListening_Throws()
    {
        int port;
        await using (var listener = await new TcpTransport().ListenAsync(Loopback, Ct))
        {
            port = TcpTransport.ParseAddress(listener.LocalEndpoint).Port;
        }

        await Assert.ThrowsAsync<SocketException>(
            () => new TcpTransport().ConnectAsync(TcpTransport.CreateEndpoint("127.0.0.1", port), Ct));
    }

    [Theory]
    [InlineData("example.org", "tcp://example.org:8110")]
    [InlineData("10.0.0.2:9000", "tcp://10.0.0.2:9000")]
    [InlineData(" localhost:1 ", "tcp://localhost:1")]
    [InlineData("::1", "tcp://[::1]:8110")]
    [InlineData("[::1]:9000", "tcp://[::1]:9000")]
    public void TryCreateEndpoint_ParsesHostAndOptionalPort(string input, string expected)
    {
        Assert.True(TcpTransport.TryCreateEndpoint(input, 8110, out var endpoint));
        Assert.Equal(expected, endpoint!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("host:")]
    [InlineData("host:0")]
    [InlineData("host:70000")]
    [InlineData("host:abc")]
    [InlineData("[::1")]
    [InlineData("[::1]x")]
    [InlineData("two words")]
    public void TryCreateEndpoint_RejectsMalformedInput(string input)
    {
        Assert.False(TcpTransport.TryCreateEndpoint(input, 8110, out _));
    }

    [Fact]
    public async Task Watchdog_CompletesWhenProcessExits()
    {
        using var process = Process.Start(new ProcessStartInfo("dotnet", "--version")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;

        await ParentProcessWatchdog.WaitForExitAsync(process.Id, Ct).WaitAsync(WaitTimeout, Ct);

        await process.WaitForExitAsync(Ct).WaitAsync(WaitTimeout, Ct);
    }

    [Fact]
    public async Task Watchdog_CompletesImmediatelyForUnknownProcess()
    {
        await ParentProcessWatchdog.WaitForExitAsync(int.MaxValue, Ct).WaitAsync(WaitTimeout, Ct);
    }

    private static async Task<ReadOnlyMemory<byte>> ReadAsync(ITransportConnection connection)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(WaitTimeout);
        return await connection.Incoming.ReadAsync(timeout.Token);
    }

    private static async Task<Socket> ConnectRawSocketAsync(ITransportListener listener)
    {
        var (host, port) = TcpTransport.ParseAddress(listener.LocalEndpoint);
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        await socket.ConnectAsync(host, port, Ct);
        return socket;
    }

    private static byte[] Frame(byte[] payload)
    {
        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);
        return frame;
    }
}
