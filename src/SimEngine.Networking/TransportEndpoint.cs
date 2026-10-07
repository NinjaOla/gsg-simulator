using System.Diagnostics.CodeAnalysis;

namespace SimEngine.Networking;

/// <summary>
/// Transport-neutral address, written as <c>scheme://address</c>, e.g.
/// <c>inproc://local</c>, <c>tcp://127.0.0.1:8110</c>, or <c>steam://76561198000000000</c>.
/// The scheme selects the <see cref="ITransport"/>; the address is transport-specific.
/// </summary>
public sealed record TransportEndpoint
{
    private const string Separator = "://";

    public TransportEndpoint(string scheme, string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        Scheme = scheme.ToLowerInvariant();
        Address = address;
    }

    /// <summary>Lower-case transport scheme.</summary>
    public string Scheme { get; }

    /// <summary>Transport-specific address.</summary>
    public string Address { get; }

    public static TransportEndpoint Parse(string value) =>
        TryParse(value, out var endpoint)
            ? endpoint
            : throw new FormatException($"'{value}' is not a valid endpoint (expected scheme://address).");

    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out TransportEndpoint? endpoint)
    {
        endpoint = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var index = value.IndexOf(Separator, StringComparison.Ordinal);
        if (index <= 0 || index + Separator.Length >= value.Length)
        {
            return false;
        }

        endpoint = new TransportEndpoint(value[..index], value[(index + Separator.Length)..]);
        return true;
    }

    public override string ToString() => $"{Scheme}{Separator}{Address}";
}
