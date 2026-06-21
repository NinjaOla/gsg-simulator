using SimEngine.Server;
using Xunit;

namespace SimEngine.Server.Tests;

public sealed class SimEngineSiloOptionsTests
{
    [Fact]
    public void Defaults_MatchOrleansLocalhostClusteringPorts()
    {
        var options = new SimEngineSiloOptions();

        Assert.Equal(11111, options.SiloPort);
        Assert.Equal(30000, options.GatewayPort);
        Assert.True(options.EnableStreams);
    }
}
