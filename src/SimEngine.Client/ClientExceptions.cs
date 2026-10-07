using SimEngine.Contracts.Wire;

namespace SimEngine.Client;

/// <summary>The server rejected a request for a reason without a more specific exception type.</summary>
public sealed class RemoteRequestException : Exception
{
    public RemoteRequestException(WireErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public WireErrorCode Code { get; }
}

/// <summary>The connection to the server closed before a request completed.</summary>
public sealed class ConnectionLostException : IOException
{
    public ConnectionLostException(string reason)
        : base(reason)
    {
    }
}
