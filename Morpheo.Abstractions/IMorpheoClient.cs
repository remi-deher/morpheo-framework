namespace Morpheo.Abstractions;

public interface IMorpheoClient
{
    Task SendPrintJobAsync(PeerInfo target, string content);

    // On utilise Task tout court pour simplifier (Fire & Forget)
    Task SendSyncUpdateAsync(PeerInfo target, SyncLogDto log);
}