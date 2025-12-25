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

    private readonly List<PeerInfo> _peers = new();
    public event EventHandler<SyncLog>? DataReceived;

    public DataSyncService(
        IServiceProvider serviceProvider,
        IMorpheoClient client,
        INetworkDiscovery discovery,
        MorpheoOptions options, // Nécessaire pour connaître notre propre NodeName
        ILogger<DataSyncService> logger)
    {
        _serviceProvider = serviceProvider;
        _client = client;
        _discovery = discovery;
        _options = options;
        _logger = logger;

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

    // --- 1. ENVOI (BROADCAST) ---
    public async Task BroadcastChangeAsync<T>(T entity, string action) where T : MorpheoEntity
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();

        // 1. Récupérer l'ancien vecteur s'il existe pour cet ID (pour conserver l'historique causal)
        var lastLog = await db.SyncLogs
            .Where(l => l.EntityId == entity.Id)
            .OrderByDescending(l => l.Timestamp)
            .FirstOrDefaultAsync();

        // Si on a déjà un vecteur, on le reprend, sinon on part de zéro
        var vector = lastLog != null ? lastLog.Vector : new VectorClock();

        // 2. Incrémenter MON compteur dans le vecteur (Moi, j'ai fait une modif)
        vector.Increment(_options.NodeName);

        var log = new SyncLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityId = entity.Id,
            EntityName = typeof(T).Name,
            Action = action,
            JsonData = JsonSerializer.Serialize(entity),
            Timestamp = DateTime.UtcNow.Ticks,
            IsFromRemote = false,
            Vector = vector // La propriété Vector s'occupe de la sérialisation JSON pour la BDD
        };

        db.SyncLogs.Add(log);
        await db.SaveChangesAsync();

        // On lance la diffusion en arrière-plan
        _ = Task.Run(() => PushToPeers(log));
    }

    private async Task PushToPeers(SyncLog log)
    {
        PeerInfo[] targets;
        lock (_peers) targets = _peers.ToArray();
        if (targets.Length == 0) return;

        // 🔧 Mapping du Vecteur vers le DTO pour l'envoi
        var dto = new SyncLogDto(
            log.Id,
            log.EntityId,
            log.EntityName,
            log.JsonData,
            log.Action,
            log.Timestamp,
            log.Vector, // <-- On passe le dictionnaire complet
            _options.NodeName // OriginNodeId
        );

        foreach (var peer in targets)
        {
            try { await _client.SendSyncUpdateAsync(peer, dto); } catch { /* Ignoré */ }
        }
    }

    // --- 2. RÉCEPTION (APPLY) ---
    public async Task ApplyRemoteChangeAsync(SyncLogDto remoteDto)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();

        // A. Anti-Doublon strict (ID unique du log déjà traité ?)
        if (await db.SyncLogs.AnyAsync(l => l.Id == remoteDto.Id)) return;

        // B. Récupération de l'état local actuel pour cet objet (la "Vérité locale")
        var localLog = await db.SyncLogs
            .Where(l => l.EntityId == remoteDto.EntityId)
            .OrderByDescending(l => l.Timestamp)
            .FirstOrDefaultAsync();

        // C. Comparaison des vecteurs
        bool shouldApply = false;

        if (localLog == null)
        {
            // On ne connait pas cet objet, c'est une création ou une première synchro -> On prend.
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
                    // Le distant est un descendant (plus récent et connait mon état) -> ON APPLIQUE
                    shouldApply = true;
                    break;

                case VectorRelation.Causes:
                    // Le distant est un ancêtre (plus vieux que moi) -> ON IGNORE
                    _logger.LogInformation($"🛡️ Ignoré : Donnée reçue obsolète pour {remoteDto.EntityName} ({remoteDto.EntityId})");
                    return;

                case VectorRelation.Concurrent:
                case VectorRelation.Equal:
                    // CONFLIT : A et B ont bougé indépendamment.
                    // Pour l'instant, on utilise le Timestamp (Last Write Wins)
                    if (remoteDto.Timestamp > localLog.Timestamp)
                    {
                        _logger.LogWarning($"🔀 Conflit sur {remoteDto.EntityName} résolu par Timestamp (Distant gagne)");
                        shouldApply = true;
                    }
                    else
                    {
                        _logger.LogWarning($"🔀 Conflit sur {remoteDto.EntityName} résolu par Timestamp (Local gagne)");
                        return;
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
                JsonData = remoteDto.JsonData,
                Action = remoteDto.Action,
                Timestamp = remoteDto.Timestamp,
                IsFromRemote = true,
                VectorClockJson = JsonSerializer.Serialize(remoteDto.VectorClock) // On stocke le vecteur reçu
            };

            db.SyncLogs.Add(newLog);
            await db.SaveChangesAsync();

            // On notifie l'application qu'une nouvelle donnée est arrivée
            DataReceived?.Invoke(this, newLog);
        }
    }

    // --- 3. COLD SYNC (Rattrapage d'historique) ---
    private async Task SynchronizeWithPeerAsync(PeerInfo peer)
    {
        _logger.LogInformation($"🔄 DEBUT COLD SYNC avec {peer.Name}...");

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

            bool moreDataAvailable = true;
            int totalSynced = 0;

            while (moreDataAvailable)
            {
                var batch = await _client.GetHistoryAsync(peer, lastTick);

                if (batch == null)
                {
                    _logger.LogWarning($"⚠️ Echec Cold Sync avec {peer.Name} (Erreur distante).");
                    return;
                }

                if (batch.Count == 0)
                {
                    moreDataAvailable = false;
                }
                else
                {
                    foreach (var logDto in batch)
                    {
                        // La logique vectorielle est gérée ici
                        await ApplyRemoteChangeAsync(logDto);

                        if (logDto.Timestamp > lastTick)
                            lastTick = logDto.Timestamp;
                    }

                    totalSynced += batch.Count;
                    if (batch.Count < 500) moreDataAvailable = false;
                }
            }

            if (totalSynced > 0)
                _logger.LogInformation($"✅ COLD SYNC TERMINÉ. {totalSynced} éléments reçus.");
            else
                _logger.LogInformation("✅ Déjà à jour.");
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ CRASH COLD SYNC : {ex.Message}");
        }
    }
}