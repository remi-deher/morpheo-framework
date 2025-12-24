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

    public async Task<bool> SendPrintJobAsync(PeerInfo target, string content)
    {
        try
        {
            // 1. Création de l'URL cible (ex: http://192.168.1.15:54321/api/print)
            var url = $"http://{target.IpAddress}:{target.Port}/api/print";

            _logger.LogInformation($"📤 Envoi d'impression vers {target.Name} ({url})...");

            // 2. Création du client léger
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5); // Fail-fast si le nœud est injoignable

            // 3. Envoi de la donnée (Payload)
            var payload = new { Content = content, Sender = "Moi" };
            var response = await client.PostAsJsonAsync(url, payload);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("✅ Impression transmise avec succès !");
                return true;
            }
            else
            {
                _logger.LogWarning($"❌ Le voisin a refusé l'impression. Code : {response.StatusCode}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ Échec de la communication avec {target.Name} : {ex.Message}");
            return false;
        }
    }
}