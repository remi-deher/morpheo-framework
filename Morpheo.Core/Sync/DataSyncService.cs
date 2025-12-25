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
    private readonly MorpheoOptions _options;
    private readonly ILogger<DataSyncService> _logger;
    private readonly ConflictResolutionEngine _conflictEngine;
    private readonly ISyncRoutingStrategy _routingStrategy; // <--- NOUVEAU

    private readonly List<PeerInfo> _peers = new();
    public event EventHandler<SyncLog>? DataReceived;

    public DataSyncService(
        IServiceProvider serviceProvider,
        IMorpheoClient client,
        INetworkDiscovery discovery,
        MorpheoOptions options,
        ILogger<DataSyncService> logger,
        ConflictResolutionEngine conflictEngine,
        ISyncRoutingStrategy routingStrategy) // <--- INJECTION
    {
        _serviceProvider = serviceProvider;
        _client = client;
        _discovery = discovery;
        _options = options;
        _logger = logger;
        _conflictEngine = conflictEngine;
        _routingStrategy = routingStrategy;

        _discovery.PeerLost += (s, p) => { lock (_peers) { _peers.RemoveAll(x => x.Id == p.Id); } };

        _discovery.PeerFound += (s, peer) =>
        {
            lock (_peers) { if (!_peers.Any(x => x.Id == peer.Id)) _peers.Add(peer); }

            // On attend un peu avant de lancer la synchro pour laisser le serveur démarrer
            Task.Run(async () =>
            {
                await Task.Delay(new Random().Next(1000, 3000));
                await SynchronizeWithPeerAsync(peer);
            });
        };
    }

    // --- 1. ENVOI (ROUTAGE INTELLIGENT) ---
    public async Task BroadcastChangeAsync<T>(T entity, string action) where T : MorpheoEntity
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();

        var lastLog = await db.SyncLogs
            .Where(l => l.EntityId == entity.Id)
            .OrderByDescending(l => l.Timestamp)
            .FirstOrDefaultAsync();

        var vector = lastLog != null ? lastLog.Vector : new VectorClock();
        vector.Increment(_options.NodeName);

        var log = new SyncLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityId = entity.Id,
            EntityName = typeof(T).Name, // Ou via TypeResolver si besoin d'abstraction totale
            Action = action,
            JsonData = JsonSerializer.Serialize(entity),
            Timestamp = DateTime.UtcNow.Ticks,
            IsFromRemote = false,
            Vector = vector
        };

        db.SyncLogs.Add(log);
        await db.SaveChangesAsync();

        // On lance la diffusion via la stratégie
        _ = Task.Run(() => PushToPeers(log));
    }

    private async Task PushToPeers(SyncLog log)
    {
        // 1. Récupération des cibles potentielles (Voisins découverts)
        PeerInfo[] candidates;
        lock (_peers) candidates = _peers.ToArray();

        // 2. Préparation du DTO
        var dto = new SyncLogDto(
            log.Id,
            log.EntityId,
            log.EntityName,
            log.JsonData,
            log.Action,
            log.Timestamp,
            log.Vector,
            _options.NodeName
        );

        // 3. Définition de l'action d'envoi unitaire (Le "Comment")
        // Cette fonction sera appelée par la stratégie pour chaque destinataire choisi
        Func<PeerInfo, SyncLogDto, Task<bool>> sendAction = async (peer, d) =>
        {
            try
            {
                await _client.SendSyncUpdateAsync(peer, d);
                return true; // Succès (ACK implicite HTTP 200)
            }
            catch
            {
                return false; // Echec
            }
        };

        // 4. Exécution de la Stratégie (Le "Qui" et "Dans quel ordre")
        // C'est ici que la magie du Failover opère (Serveur -> Mesh, etc.)
        await _routingStrategy.PropagateAsync(dto, candidates, sendAction);
    }

    // --- 2. RÉCEPTION (AVEC RÉSOLUTION AGNOSTIQUE) ---
    public async Task ApplyRemoteChangeAsync(SyncLogDto remoteDto)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();

        if (await db.SyncLogs.AnyAsync(l => l.Id == remoteDto.Id)) return;

        var localLog = await db.SyncLogs
            .Where(l => l.EntityId == remoteDto.EntityId)
            .OrderByDescending(l => l.Timestamp)
            .FirstOrDefaultAsync();

        bool shouldApply = false;
        string finalJsonData = remoteDto.JsonData;

        if (localLog == null)
        {
            shouldApply = true;
        }
        else
        {
            var localVector = localLog.Vector;
            var remoteVector = new VectorClock(remoteDto.VectorClock);
            var relation = localVector.CompareTo(remoteVector);

            switch (relation)
            {
                case VectorRelation.CausedBy:
                    shouldApply = true;
                    break;

                case VectorRelation.Causes:
                    return;

                case VectorRelation.Concurrent:
                case VectorRelation.Equal:
                    // Utilisation du Moteur de Résolution Agnostique
                    finalJsonData = _conflictEngine.Resolve(
                        remoteDto.EntityName,
                        localLog.JsonData,
                        localLog.Timestamp,
                        remoteDto.JsonData,
                        remoteDto.Timestamp
                    );

                    if (finalJsonData != localLog.JsonData)
                    {
                        shouldApply = true;
                        _logger.LogInformation($"🔀 Conflit résolu (Merge/LWW) pour {remoteDto.EntityName}");
                    }
                    break;
            }
        }

        if (shouldApply)
        {
            var newLog = new SyncLog
            {
                Id = remoteDto.Id,
                EntityId = remoteDto.EntityId,
                EntityName = remoteDto.EntityName,
                JsonData = finalJsonData,
                Action = remoteDto.Action,
                Timestamp = remoteDto.Timestamp,
                IsFromRemote = true,
                VectorClockJson = JsonSerializer.Serialize(remoteDto.VectorClock)
            };

            db.SyncLogs.Add(newLog);
            await db.SaveChangesAsync();

            DataReceived?.Invoke(this, newLog);
        }
    }

    // --- 3. COLD SYNC (Rattrapage) ---
    private async Task SynchronizeWithPeerAsync(PeerInfo peer)
    {
        // ... (Logique Cold Sync inchangée)
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

            // Note : Pour le Cold Sync, on tire généralement depuis n'importe quel pair disponible.
            // On pourrait aussi utiliser une stratégie ici, mais souvent le P2P suffit.
            var batch = await _client.GetHistoryAsync(peer, lastTick);
            if (batch != null && batch.Count > 0)
            {
                foreach (var logDto in batch)
                {
                    await ApplyRemoteChangeAsync(logDto);
                }
                _logger.LogInformation($"✅ COLD SYNC : {batch.Count} éléments reçus de {peer.Name}.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ Erreur Cold Sync avec {peer.Name} : {ex.Message}");
        }
    }
}