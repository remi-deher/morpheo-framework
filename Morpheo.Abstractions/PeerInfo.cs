namespace Morpheo.Abstractions;

public record PeerInfo(string Id, string Name, string IpAddress, int Port, NodeRole Role, string[] Tags);