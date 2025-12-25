using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;
using Morpheo.Core.Data;
using Morpheo.Core.Sync;
using System.Text;
using System.Text.Json; // ⚠️ Nécessaire pour JsonSerializer

namespace Morpheo.Core.Server;

public class MorpheoWebServer
{
    private readonly MorpheoOptions _options;
    private readonly ILogger<MorpheoWebServer> _logger;
    private readonly INetworkDiscovery _discovery;
    private readonly DataSyncService _syncService;
    private readonly IServiceProvider _mainServiceProvider;

    private WebApplication? _app;
    public int LocalPort { get; private set; }

    public MorpheoWebServer(
        MorpheoOptions options,
        ILogger<MorpheoWebServer> logger,
        INetworkDiscovery discovery,
        DataSyncService syncService,
        IServiceProvider mainServiceProvider)
    {
        _options = options;
        _logger = logger;
        _discovery = discovery;
        _syncService = syncService;
        _mainServiceProvider = mainServiceProvider;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(0));

        _app = builder.Build();

        _app.MapGet("/api/ping", () => Results.Ok($"Pong from {_options.NodeName}"));

        // HOT SYNC (Push)
        _app.MapPost("/api/sync", async ([FromBody] SyncLogDto dto) =>
        {
            try
            {
                await _syncService.ApplyRemoteChangeAsync(dto);
                return Results.Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur réception Sync : {ex.Message}");
                return Results.Problem(ex.Message);
            }
        });

        // COLD SYNC (Pull)
        _app.MapGet("/api/sync/history", async (long since) =>
        {
            using var scope = _mainServiceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();

            try
            {
                // 1. On récupère les données brutes de la BDD (Entités SyncLog)
                var logs = await db.SyncLogs
                    .AsNoTracking()
                    .Where(l => l.Timestamp > since)
                    .OrderBy(l => l.Timestamp)
                    .Take(500)
                    .ToListAsync(); // ⚠️ On exécute la requête ici

                // 2. On transforme en DTO en mémoire (pour pouvoir désérialiser le JSON du vecteur)
                var dtos = logs.Select(l => new SyncLogDto(
                    l.Id,
                    l.EntityId,
                    l.EntityName,
                    l.JsonData,
                    l.Action,
                    l.Timestamp,
                    // Conversion du JSON stocké en BDD vers le Dictionnaire du DTO
                    string.IsNullOrEmpty(l.VectorClockJson)
                        ? new Dictionary<string, long>()
                        : JsonSerializer.Deserialize<Dictionary<string, long>>(l.VectorClockJson, (JsonSerializerOptions?)null) ?? new Dictionary<string, long>(),
                    "" // OriginNodeId (à remplir plus tard si besoin)
                ));

                return Results.Ok(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur History");
                return Results.Problem(ex.Message, statusCode: 500);
            }
        });

        _app.MapGet("/morpheo/dashboard", () => Results.Content("<h1>Dashboard</h1>", "text/html"));

        await _app.StartAsync(ct);
        LocalPort = _app.Urls.Select(u => new Uri(u).Port).FirstOrDefault();
    }

    public async Task StopAsync()
    {
        if (_app != null) await _app.StopAsync();
    }
}