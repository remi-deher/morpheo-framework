using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;
using Morpheo.Core.Data;
using Morpheo.Core.Security; // Nécessaire pour l'authentificateur
using Morpheo.Core.Sync;

namespace Morpheo.Core.Server;

public class MorpheoWebServer
{
    private readonly MorpheoOptions _options;
    private readonly ILogger<MorpheoWebServer> _logger;
    private readonly INetworkDiscovery _discovery;
    private readonly DataSyncService _syncService;
    private readonly IServiceProvider _mainServiceProvider;

    // Le "Videur" (Injecté via l'interface)
    private readonly IRequestAuthenticator _authenticator;

    private WebApplication? _app;
    public int LocalPort { get; private set; }

    public MorpheoWebServer(
        MorpheoOptions options,
        ILogger<MorpheoWebServer> logger,
        INetworkDiscovery discovery,
        DataSyncService syncService,
        IServiceProvider mainServiceProvider,
        IRequestAuthenticator authenticator) // <--- INJECTION
    {
        _options = options;
        _logger = logger;
        _discovery = discovery;
        _syncService = syncService;
        _mainServiceProvider = mainServiceProvider;
        _authenticator = authenticator;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(0));

        _app = builder.Build();

        // --- MIDDLEWARE DE SÉCURITÉ (Agnostique) ---
        // C'est ici qu'on vérifie l'identité avant de traiter la requête.
        _app.Use(async (context, next) =>
        {
            // On protège uniquement les API critiques (Sync, Print).
            // On laisse passer /morpheo/dashboard ou /api/ping si besoin.
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                // On délègue la décision à l'authentificateur injecté (AllowAll ou HMAC)
                if (!await _authenticator.IsAuthorizedAsync(context))
                {
                    context.Response.StatusCode = 401; // Unauthorized
                    await context.Response.WriteAsync("⛔ Access Denied: Authentication Failed");
                    _logger.LogWarning($"⛔ Rejet connexion non autorisée depuis {context.Connection.RemoteIpAddress}");
                    return;
                }
            }
            await next();
        });

        // --- ROUTES API ---

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
                // 1. Récupération BDD
                var logs = await db.SyncLogs
                    .AsNoTracking()
                    .Where(l => l.Timestamp > since)
                    .OrderBy(l => l.Timestamp)
                    .Take(500)
                    .ToListAsync();

                // 2. Conversion DTO
                var dtos = logs.Select(l => new SyncLogDto(
                    l.Id,
                    l.EntityId,
                    l.EntityName,
                    l.JsonData,
                    l.Action,
                    l.Timestamp,
                    string.IsNullOrEmpty(l.VectorClockJson)
                        ? new Dictionary<string, long>()
                        : JsonSerializer.Deserialize<Dictionary<string, long>>(l.VectorClockJson, (JsonSerializerOptions?)null) ?? new Dictionary<string, long>(),
                    _options.NodeName
                ));

                return Results.Ok(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur History");
                return Results.Problem(ex.Message, statusCode: 500);
            }
        });

        // Route Impression (Optionnelle, utilise PrintGateway)
        _app.MapPost("/api/print", async (HttpContext ctx, [FromServices] IPrintGateway printer) =>
        {
            // TODO: Désérialiser la demande d'impression
            return Results.Ok();
        });

        _app.MapGet("/morpheo/dashboard", () => Results.Content("<h1>Morpheo Node Dashboard</h1>", "text/html"));

        await _app.StartAsync(ct);
        LocalPort = _app.Urls.Select(u => new Uri(u).Port).FirstOrDefault();
    }

    public async Task StopAsync()
    {
        if (_app != null) await _app.StopAsync();
    }
}