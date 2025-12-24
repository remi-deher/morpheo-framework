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

    // On garde une liste locale des voisins pour éviter d'interroger le discovery à chaque fois
    private readonly List<PeerInfo> _peers = new();

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

        // Mise à jour de la liste des voisins en temps réel
        _discovery.PeerFound += (s, p) => { lock (_peers) { if (!_peers.Any(x => x.Id == p.Id)) _peers.Add(p); } };
        _discovery.PeerLost += (s, p) => { lock (_peers) { _peers.RemoveAll(x => x.Id == p.Id); } };
    }

    /// <summary>
    /// À appeler quand vous modifiez une donnée localement (PUSH)
    /// </summary>
    public async Task BroadcastChangeAsync<T>(T entity, string action) where T : MorpheoEntity
    {
        // 1. Création du Log
        var log = new SyncLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityId = entity.Id,
            EntityName = typeof(T).Name,
            Action = action,
            JsonData = JsonSerializer.Serialize(entity),
            Timestamp = DateTime.UtcNow.Ticks, // L'heure de référence
            IsFromRemote = false
        };

        // 2. Sauvegarde Locale (Historique)
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();
            db.SyncLogs.Add(log);
            await db.SaveChangesAsync();
        }

        // 3. Propagation asynchrone (Fire & Forget)
        _ = Task.Run(() => PushToPeers(log));
    }

    private async Task PushToPeers(SyncLog log)
    {
        PeerInfo[] targets;
        lock (_peers) targets = _peers.ToArray();

        if (targets.Length == 0) return;

        var dto = new SyncLogDto(log.Id, log.EntityId, log.EntityName, log.JsonData, log.Action, log.Timestamp);

        _logger.LogInformation($"🔄 Sync {log.EntityName} vers {targets.Length} voisins...");

        foreach (var peer in targets)
        {
            // On n'attend pas la réponse pour ne pas ralentir la boucle
            try { await _client.SendSyncUpdateAsync(peer, dto); }
            catch { /* Géré dans le client */ }
        }
    }

    /// <summary>
    /// Appelé quand on reçoit une donnée d'un voisin (PULL/RECEIVE)
    /// </summary>
    public async Task ApplyRemoteChangeAsync(SyncLogDto remoteDto)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();

        // 1. Idempotence : A-t-on déjà traité ce message exact ?
        if (await db.SyncLogs.AnyAsync(l => l.Id == remoteDto.Id))
            return; // Déjà vu, on ignore.

        // 2. Gestion de Conflit (Last Write Wins)
        // On regarde si on a une version plus récente de cet objet localement
        var existingLog = await db.SyncLogs
            .Where(l => l.EntityId == remoteDto.EntityId)
            .OrderByDescending(l => l.Timestamp)
            .FirstOrDefaultAsync();

        if (existingLog != null && existingLog.Timestamp > remoteDto.Timestamp)
        {
            _logger.LogWarning($"⚔️ Conflit gagné (Local plus récent) pour {remoteDto.EntityName} : {remoteDto.EntityId}");
            return; // Notre version est plus récente, on ignore celle du voisin.
        }

        _logger.LogInformation($"📥 Mise à jour acceptée : {remoteDto.EntityName} ({remoteDto.Action})");

        // 3. On enregistre le log
        var newLog = new SyncLog
        {
            Id = remoteDto.Id,
            EntityId = remoteDto.EntityId,
            EntityName = remoteDto.EntityName,
            JsonData = remoteDto.JsonData,
            Action = remoteDto.Action,
            Timestamp = remoteDto.Timestamp,
            IsFromRemote = true // IMPORTANT : Ne pas re-broadcaster !
        };
        db.SyncLogs.Add(newLog);

        // 4. On applique la modification sur la VRAIE table (Pas juste le log)
        // Note : C'est ici qu'on ferait de la réflexion ou un mapping dynamique.
        // Pour ce framework, on stocke juste le log, l'application hôte pourra réagir via un événement si besoin.

        await db.SaveChangesAsync();
    }
}