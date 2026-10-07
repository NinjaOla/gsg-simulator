using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimEngine.Contracts.Wire;

/// <summary>
/// Encodes and decodes wire messages. The only place that knows the wire
/// format (JSON today), so switching to MessagePack stays local to this file.
/// </summary>
public static class WireCodec
{
    public static byte[] Encode(ClientMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes(message, WireJsonContext.Default.ClientMessage);

    public static byte[] Encode(ServerMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes(message, WireJsonContext.Default.ServerMessage);

    /// <summary>Throws <see cref="WireFormatException"/> on malformed or unknown messages.</summary>
    public static ClientMessage DecodeClient(ReadOnlySpan<byte> data) =>
        Decode(data, WireJsonContext.Default.ClientMessage);

    /// <summary>Throws <see cref="WireFormatException"/> on malformed or unknown messages.</summary>
    public static ServerMessage DecodeServer(ReadOnlySpan<byte> data) =>
        Decode(data, WireJsonContext.Default.ServerMessage);

    private static T Decode<T>(ReadOnlySpan<byte> data, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(data, typeInfo)
                ?? throw new WireFormatException("Empty message.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            throw new WireFormatException($"Malformed {typeof(T).Name}: {ex.Message}", ex);
        }
    }
}

/// <summary>A received message could not be decoded.</summary>
public sealed class WireFormatException : Exception
{
    public WireFormatException(string message)
        : base(message)
    {
    }

    public WireFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ClientMessage))]
[JsonSerializable(typeof(ServerMessage))]
internal sealed partial class WireJsonContext : JsonSerializerContext;
