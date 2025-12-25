namespace Morpheo.Abstractions;

public interface INetworkDiscovery
{
    /// <summary>
    /// Démarre la diffusion de notre présence (Hello packets) en arrière-plan.
    /// Ne doit pas bloquer l'exécution.
    /// </summary>
    Task StartAdvertisingAsync(PeerInfo myInfo, CancellationToken ct);

    /// <summary>
    /// Démarre l'écoute des paquets entrants en arrière-plan.
    /// Ne doit pas bloquer l'exécution.
    /// </summary>
    Task StartListeningAsync(CancellationToken ct);

    /// <summary>
    /// Arrête proprement la découverte et libère les sockets.
    /// </summary>
    void Stop();

    event EventHandler<PeerInfo> PeerFound;
    event EventHandler<PeerInfo> PeerLost;

    IReadOnlyList<PeerInfo> GetPeers();
}