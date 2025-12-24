using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;

namespace Morpheo.Core.Client;

public class MorpheoHttpClient : IMorpheoClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MorpheoHttpClient> _logger;

    public MorpheoHttpClient(IHttpClientFactory httpClientFactory, ILogger<MorpheoHttpClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task SendPrintJobAsync(PeerInfo target, string content)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var url = $"http://{target.IpAddress}:{target.Port}/api/print";
            var request = new { Content = content, Sender = "Unknown" };

            await client.PostAsJsonAsync(url, request);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Échec envoi print vers {target.Name} : {ex.Message}");
            // On ne throw pas ici pour ne pas crasher l'appelant, ou throw si vous préférez gérer l'erreur plus haut.
        }
    }

    public async Task SendSyncUpdateAsync(PeerInfo target, SyncLogDto log)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(2);

            var url = $"http://{target.IpAddress}:{target.Port}/api/sync";

            // On envoie et on oublie (Fire & Forget), pas besoin de retourner bool
            await client.PostAsJsonAsync(url, log);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Échec envoi Sync vers {target.Name} : {ex.Message}");
        }
    }
}