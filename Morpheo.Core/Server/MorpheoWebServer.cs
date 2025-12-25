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

namespace Morpheo.Core.Server;

public record PrintRequest(string Content, string Sender);

public class MorpheoWebServer
{
    private readonly MorpheoOptions _options;
    private readonly ILogger<MorpheoWebServer> _logger;
    private readonly INetworkDiscovery _discovery;
    private readonly DataSyncService _syncService;

    // 🔑 AJOUT : On garde une référence vers le conteneur principal (celui de l'App WPF)
    private readonly IServiceProvider _mainServiceProvider;

    private WebApplication? _app;

    public int LocalPort { get; private set; }

    public MorpheoWebServer(
        MorpheoOptions options,
        ILogger<MorpheoWebServer> logger,
        INetworkDiscovery discovery,
        DataSyncService syncService,
        IServiceProvider mainServiceProvider) // 💉 INJECTION DU CONTENEUR PRINCIPAL
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

        // --- API : Ping ---
        _app.MapGet("/api/ping", () => Results.Ok($"Pong from {_options.NodeName}"));

        // --- API : Print ---
        _app.MapPost("/api/print", (PrintRequest request) =>
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"🖨️ [ORDRE REÇU de {request.Sender}] : \"{request.Content}\"");
            Console.ResetColor();
            return Results.Ok(new { status = "Printed" });
        });

        // --- API : Sync (PUSH) ---
        _app.MapPost("/api/sync", async ([FromBody] SyncLogDto dto) =>
        {
            try
            {
                // _syncService est déjà résolu, donc pas de problème ici
                await _syncService.ApplyRemoteChangeAsync(dto);
                return Results.Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur réception Sync : {ex.Message}");
                return Results.Problem(ex.Message);
            }
        });

        // --- API : Sync History (PULL) ---
        // On retire "IServiceProvider sp" des paramètres car on utilise _mainServiceProvider
        _app.MapGet("/api/sync/history", async (long since) =>
        {
            // 🔑 FIX : On utilise _mainServiceProvider pour créer le scope.
            // Cela garantit qu'on accède au DbContext configuré dans App.xaml.cs
            using var scope = _mainServiceProvider.CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();

            // On peut récupérer un logger frais ou utiliser _logger
            var reqLogger = scope.ServiceProvider.GetRequiredService<ILogger<MorpheoWebServer>>();

            const int BATCH_SIZE = 500;

            try
            {
                var logs = await db.SyncLogs
                    .AsNoTracking()
                    .Where(l => l.Timestamp > since)
                    .OrderBy(l => l.Timestamp)
                    .Take(BATCH_SIZE)
                    .Select(l => new SyncLogDto(
                        l.Id,
                        l.EntityId,
                        l.EntityName,
                        l.JsonData,
                        l.Action,
                        l.Timestamp
                    ))
                    .ToListAsync();

                return Results.Ok(logs);
            }
            catch (Exception ex)
            {
                // Important : on loggue l'erreur pour voir la vraie cause (ex: SQLite locked)
                reqLogger.LogError(ex, "Erreur critique lors de l'export de l'historique.");
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });

        // --- DASHBOARD ---
        _app.MapGet("/morpheo/dashboard", () => Results.Content(GenerateDashboardHtml(), "text/html"));

        await _app.StartAsync(ct);

        LocalPort = _app.Urls.Select(u => new Uri(u).Port).FirstOrDefault();
        _logger.LogInformation($"🌍 Dashboard : http://localhost:{LocalPort}/morpheo/dashboard");
    }

    private string GenerateDashboardHtml()
    {
        // ... (Garder votre code HTML existant ici) ...
        var peers = _discovery.GetPeers();
        var sb = new StringBuilder();
        // ... Code HTML ...
        return "<h1>Dashboard (Code abrégé pour lisibilité)</h1>"; // Remettez votre fonction complète ici
    }

    public async Task StopAsync()
    {
        if (_app != null) await _app.StopAsync();
    }
}