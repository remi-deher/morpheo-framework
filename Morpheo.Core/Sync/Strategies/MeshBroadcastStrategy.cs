using Morpheo.Abstractions;

namespace Morpheo.Core.Sync.Strategies;

/// <summary>
/// Stratégie par défaut : Inonde le réseau (Gossip).
/// Idéal pour le mode "Mesh Pur" sans serveur central.
/// </summary>
public class MeshBroadcastStrategy : ISyncRoutingStrategy
{
    public async Task PropagateAsync(
        SyncLogDto log,
        IEnumerable<PeerInfo> candidates,
        Func<PeerInfo, SyncLogDto, Task<bool>> sendFunc)
    {
        // On envoie à tout le monde en parallèle
        var tasks = candidates.Select(peer => sendFunc(peer, log));
        await Task.WhenAll(tasks);
    }
}