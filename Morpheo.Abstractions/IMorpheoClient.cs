namespace Morpheo.Abstractions;

public interface IMorpheoClient
{
    // Envoie un ordre d'impression à un nœud spécifique
    Task<bool> SendPrintJobAsync(PeerInfo target, string content);
}