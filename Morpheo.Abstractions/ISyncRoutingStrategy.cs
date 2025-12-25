namespace Morpheo.Abstractions;

/// <summary>
/// Définit la stratégie de propagation des données dans le réseau.
/// Permet d'implémenter des logiques comme "Serveur Unique", "Mesh Total", ou "Cascade (Failover)".
/// </summary>
public interface ISyncRoutingStrategy
{
    /// <summary>
    /// Exécute la propagation d'un log vers les cibles appropriées.
    /// </summary>
    /// <param name="log">La donnée à envoyer.</param>
    /// <param name="candidates">La liste des pairs actuellement visibles (via Discovery).</param>
    /// <param name="sendFunc">
    /// Un délégué qui effectue l'envoi réel. 
    /// Retourne TRUE si l'envoi a réussi (ACK), FALSE sinon.
    /// </param>
    Task PropagateAsync(
        SyncLogDto log,
        IEnumerable<PeerInfo> candidates,
        Func<PeerInfo, SyncLogDto, Task<bool>> sendFunc
    );
}