using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;

namespace Morpheo.Core.Client;

public class MorpheoHttpClient : IMorpheoClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MorpheoHttpClient> _logger;
    private readonly MorpheoOptions _options; // 1. On injecte les options

    public MorpheoHttpClient(
        IHttpClientFactory httpClientFactory,
        ILogger<MorpheoHttpClient> logger,
        MorpheoOptions options)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _options = options;
    }

    private string BuildUrl(PeerInfo target, string path)
    {
        // 2. Convention : on respecte le choix de sécurité
        string scheme = _options.UseSecureConnection ? "https" : "http";

        var address = target.IpAddress;
        if (address.Contains(":") && !address.Contains("["))
        {
            address = $"[{address}]";
        }

        return $"{scheme}://{address}:{target.Port}{path}";
    }

    public async Task SendPrintJobAsync(PeerInfo target, string content)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var url = BuildUrl(target, "/api/print");

            var request = new { Content = content, Sender = _options.NodeName };
            await client.PostAsJsonAsync(url, request);
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ Erreur PRINT vers {target.Name} : {ex.Message}");
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
            _logger.LogDebug($"Sync échouée vers {target.Name} (Normal si déconnecté)");
        }
    }

    public async Task<List<SyncLogDto>> GetHistoryAsync(PeerInfo target, long sinceTick)
    {
        var url = BuildUrl(target, $"/api/sync/history?since={sinceTick}");
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            return await client.GetFromJsonAsync<List<SyncLogDto>>(url) ?? new List<SyncLogDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ ÉCHEC COLD SYNC vers {target.Name} : {ex.Message}");
            return new List<SyncLogDto>();
        }
    }
}