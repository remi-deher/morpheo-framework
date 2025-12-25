using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;
using Morpheo.Core.Data;

namespace Morpheo.Core.Sync;

public class DataSyncService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMorpheoClient _client;
    private readonly INetworkDiscovery _discovery;
    private readonly ILogger<DataSyncService> _logger;

    private readonly List<PeerInfo> _peers = new();
    public event EventHandler<SyncLog>? DataReceived;

    public DataSyncService(
        IServiceProvider serviceProvider,
        IMorpheoClient client,
        INetworkDiscovery discovery,
        ILogger<DataSyncService> logger)
    {
        _serviceProvider = serviceProvider;
        _client = client;
        _discovery = discovery;
        _logger = logger;

        // Gestion de la liste des voisins
        _discovery.PeerLost += (s, p) => { lock (_peers) { _peers.RemoveAll(x => x.Id == p.Id); } };

        // Initial Sync
        _discovery.PeerFound += (s, peer) =>
        {
            lock (_peers) { if (!_peers.Any(x => x.Id == peer.Id)) _peers.Add(peer); }
            Task.Run(async () => await SynchronizeWithPeerAsync(peer));
        };
    }

    /// <summary>
    /// Récupère l'historique manquant par paquets (Pagination)
    /// </summary>
    private async Task SynchronizeWithPeerAsync(PeerInfo peer)
    {
        _logger.LogInformation($"🔄 DEBUT COLD SYNC avec {peer.Name} ({peer.IpAddress}:{peer.Port})...");

        try
        {
            long lastTick = 0;
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();
                if (await db.SyncLogs.AnyAsync())
                {
                    lastTick = await db.SyncLogs.MaxAsync(l => l.Timestamp);
                }
            }

            _logger.LogInformation($"📅 Ma dernière donnée date de : {new DateTime(lastTick)} (Ticks: {lastTick})");

            bool moreDataAvailable = true;
            int totalSynced = 0;

            // 🔄 BOUCLE DE PAGINATION
            while (moreDataAvailable)
            {
                // On récupère un paquet de 500 max
                var batch = await _client.GetHistoryAsync(peer, lastTick);

                if (batch == null || batch.Count == 0)
                {
                    moreDataAvailable = false;
                }
                else
                {
                    _logger.LogInformation($"📥 Réception d'un paquet de {batch.Count} logs...");

                    foreach (var log in batch)
                    {
                        await ApplyRemoteChangeAsync(log);

                        // On avance le curseur pour la prochaine requête
                        if (log.Timestamp > lastTick)
                            lastTick = log.Timestamp;
                    }

                    totalSynced += batch.Count;

                    // Si on a reçu moins de 500 items, c'est que c'était le dernier paquet
                    if (batch.Count < 500)
                        moreDataAvailable = false;
                }
            }

            if (totalSynced > 0)
                _logger.LogInformation($"✅ COLD SYNC TERMINÉ. Total synchronisé : {totalSynced} éléments.");
            else
                _logger.LogInformation("✅ Déjà à jour.");
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogError($"❌ ÉCHEC RÉSEAU vers {peer.Name} : {httpEx.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ CRASH COLD SYNC : {ex.Message}");
        }
    }

    public async Task BroadcastChangeAsync<T>(T entity, string action) where T : MorpheoEntity
    {
        var log = new SyncLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityId = entity.Id,
            EntityName = typeof(T).Name,
            Action = action,
            JsonData = JsonSerializer.Serialize(entity),
            Timestamp = DateTime.UtcNow.Ticks,
            IsFromRemote = false
        };

        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();
            db.SyncLogs.Add(log);
            await db.SaveChangesAsync();
        }

        _ = Task.Run(() => PushToPeers(log));
    }

    private async Task PushToPeers(SyncLog log)
    {
        PeerInfo[] targets;
        lock (_peers) targets = _peers.ToArray();
        if (targets.Length == 0) return;

        var dto = new SyncLogDto(log.Id, log.EntityId, log.EntityName, log.JsonData, log.Action, log.Timestamp);

        foreach (var peer in targets)
        {
            try { await _client.SendSyncUpdateAsync(peer, dto); }
            catch { /* Ignoré pour ne pas bloquer */ }
        }
    }

    public async Task ApplyRemoteChangeAsync(SyncLogDto remoteDto)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();

        // 1. Déjà reçu ?
        if (await db.SyncLogs.AnyAsync(l => l.Id == remoteDto.Id)) return;

        // 2. Gestion de conflit (Last Write Wins)
        var existingLog = await db.SyncLogs
            .Where(l => l.EntityId == remoteDto.EntityId)
            .OrderByDescending(l => l.Timestamp)
            .FirstOrDefaultAsync();

        if (existingLog != null && existingLog.Timestamp > remoteDto.Timestamp)
        {
            // Notre donnée locale est plus récente, on ignore celle du voisin
            return;
        }

        var newLog = new SyncLog
        {
            Id = remoteDto.Id,
            EntityId = remoteDto.EntityId,
            EntityName = remoteDto.EntityName,
            JsonData = remoteDto.JsonData,
            Action = remoteDto.Action,
            Timestamp = remoteDto.Timestamp,
            IsFromRemote = true
        };

        db.SyncLogs.Add(newLog);
        await db.SaveChangesAsync();

        DataReceived?.Invoke(this, newLog);
    }
}