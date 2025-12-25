using System.Net;
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

    // Méthode utilitaire pour créer une URL valide (gère IPv4 et IPv6)
    private string BuildUrl(PeerInfo target, string path)
    {
        var address = target.IpAddress;

        // Si c'est une adresse IPv6 brute (ex: ::1), il faut l'entourer de crochets []
        if (address.Contains(":") && !address.Contains("["))
        {
            address = $"[{address}]";
        }

        return $"http://{address}:{target.Port}{path}";
    }

    public async Task SendPrintJobAsync(PeerInfo target, string content)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var url = BuildUrl(target, "/api/print");

            var request = new { Content = content, Sender = "Unknown" };
            await client.PostAsJsonAsync(url, request);
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ Erreur PRINT vers {target.Name} ({target.IpAddress}): {ex.Message}");
        }
    }

    public async Task SendSyncUpdateAsync(PeerInfo target, SyncLogDto log)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(2);
            var url = BuildUrl(target, "/api/sync");

            var response = await client.PostAsJsonAsync(url, log);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"⚠️ Erreur SYNC PUSH vers {target.Name}: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"❌ Erreur SYNC PUSH vers {target.Name}: {ex.Message}");
        }
    }

    public async Task<List<SyncLogDto>> GetHistoryAsync(PeerInfo target, long sinceTick)
    {
        var url = BuildUrl(target, $"/api/sync/history?since={sinceTick}");
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            _logger.LogInformation($"📞 Appel Historique : GET {url}");

            var result = await client.GetFromJsonAsync<List<SyncLogDto>>(url);

            _logger.LogInformation($"✅ Historique reçu de {target.Name} : {result?.Count ?? 0} éléments.");
            return result ?? new List<SyncLogDto>();
        }
        catch (Exception ex)
        {
            // ICI on loggue l'erreur précise
            _logger.LogError($"❌ ÉCHEC COLD SYNC vers {target.Name} ({url}) : {ex.Message}");
            return new List<SyncLogDto>();
        }
    }
}