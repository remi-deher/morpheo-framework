using System.Text.Json.Serialization;
using Morpheo.Abstractions;

namespace Morpheo.Core.Discovery;

public enum DiscoveryMessageType
{
    Hello,
    Bye
}

public class DiscoveryPacket
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("n")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("ip")]
    public string IpAddress { get; set; } = string.Empty;

    [JsonPropertyName("p")]
    public int Port { get; set; }

    [JsonPropertyName("r")]
    public NodeRole Role { get; set; }

    [JsonPropertyName("tg")]
    public string[] Tags { get; set; } = Array.Empty<string>();

    [JsonPropertyName("t")]
    public DiscoveryMessageType Type { get; set; } = DiscoveryMessageType.Hello;

    public static byte[] Serialize(DiscoveryPacket packet)
        => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(packet);

    public static DiscoveryPacket? Deserialize(byte[] data)
        => System.Text.Json.JsonSerializer.Deserialize<DiscoveryPacket>(data);
}