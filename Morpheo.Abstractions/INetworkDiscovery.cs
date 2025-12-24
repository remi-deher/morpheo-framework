namespace Morpheo.Abstractions;

public record PeerInfo(string Id, string Name, string IpAddress, NodeRole Role);

public interface INetworkDiscovery
{
    // Démarre l'écoute et l'annonce sur le réseau
    Task StartAdvertisingAsync(PeerInfo myInfo, CancellationToken ct);

    // Événement déclenché quand un voisin est trouvé
    event EventHandler<PeerInfo> PeerFound;

    // Événement quand un voisin disparait (timeout)
    event EventHandler<PeerInfo> PeerLost;
}