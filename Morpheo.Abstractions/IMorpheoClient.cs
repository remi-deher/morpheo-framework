namespace Morpheo.Abstractions;

public interface IMorpheoClient
{
    // Envoi d'un ordre d'impression (Fire & Forget)
    Task SendPrintJobAsync(PeerInfo target, string content);

    // Envoi d'une nouvelle donnée (Push)
    Task SendSyncUpdateAsync(PeerInfo target, SyncLogDto log);

    // NOUVEAU : Demande de l'historique manquant (Pull)
    Task<List<SyncLogDto>> GetHistoryAsync(PeerInfo target, long sinceTick);
}