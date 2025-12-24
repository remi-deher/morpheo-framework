namespace Morpheo.Abstractions;

public interface IMorpheoNode
{
    // Permet de démarrer le nœud (BDD + Réseau)
    Task StartAsync(CancellationToken ct = default);

    // Permet d'arrêter proprement le nœud
    Task StopAsync();

    // Donne accès à la couche de découverte (pour s'abonner aux événements PeerFound/PeerLost)
    INetworkDiscovery Discovery { get; }
}