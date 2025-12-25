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

    private WebApplication? _app;

    public int LocalPort { get; private set; }

    public MorpheoWebServer(
        MorpheoOptions options,
        ILogger<MorpheoWebServer> logger,
        INetworkDiscovery discovery,
        DataSyncService syncService)
    {
        _options = options;
        _logger = logger;
        _discovery = discovery;
        _syncService = syncService;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();

        // Nettoyage des logs par défaut
        builder.Logging.ClearProviders();

        // Écoute sur n'importe quelle IP (IPv4/IPv6) sur un port dynamique (0)
        builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(0));

        _app = builder.Build();

        // --- API : Ping ---
        _app.MapGet("/api/ping", () => Results.Ok($"Pong from {_options.NodeName}"));

        // --- API : Print (Test) ---
        _app.MapPost("/api/print", (PrintRequest request) =>
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"🖨️ [ORDRE REÇU de {request.Sender}] : \"{request.Content}\"");
            Console.ResetColor();
            return Results.Ok(new { status = "Printed" });
        });

        // --- API : Sync (PUSH - Hot Sync) ---
        _app.MapPost("/api/sync", async ([FromBody] SyncLogDto dto, IServiceProvider sp) =>
        {
            try
            {
                await _syncService.ApplyRemoteChangeAsync(dto);
                return Results.Ok();
            }
            catch (Exception ex)
            {
                var logger = sp.GetService<ILogger<MorpheoWebServer>>();
                logger?.LogError($"Erreur réception Sync : {ex.Message}");
                return Results.Problem(ex.Message);
            }
        });

        // --- API : Sync History (PULL - Cold Sync) ---
        // ✅ CORRECTION CRITIQUE : Pagination + Projection DTO
        _app.MapGet("/api/sync/history", async (long since, IServiceProvider sp) =>
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<MorpheoWebServer>>();

            const int BATCH_SIZE = 500; // Limite pour éviter l'erreur 500

            try
            {
                // On projette directement en DTO via .Select() AVANT le .ToListAsync()
                // Cela génère un SQL optimisé et évite de charger les entités lourdes en RAM
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
                logger.LogError(ex, "Erreur critique lors de l'export de l'historique.");
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });

        // --- DASHBOARD HTML ---
        _app.MapGet("/morpheo/dashboard", () => Results.Content(GenerateDashboardHtml(), "text/html"));

        await _app.StartAsync(ct);

        LocalPort = _app.Urls.Select(u => new Uri(u).Port).FirstOrDefault();
        _logger.LogInformation($"🌍 Dashboard : http://localhost:{LocalPort}/morpheo/dashboard");
    }

    private string GenerateDashboardHtml()
    {
        var peers = _discovery.GetPeers();
        var sb = new StringBuilder();

        sb.Append("<html><head><title>Morpheo Dashboard</title>");
        sb.Append("<style>body{font-family:sans-serif; background:#f0f2f5; padding:20px;} ");
        sb.Append(".card{background:white; padding:20px; margin-bottom:15px; border-radius:8px; box-shadow:0 2px 4px rgba(0,0,0,0.1);} ");
        sb.Append("h1{color:#1a73e8;} .badge{background:#e8f0fe; color:#1a73e8; padding:4px 8px; border-radius:12px; font-size:0.8em; margin-right:5px;}");
        sb.Append(".peer-row{display:flex; justify-content:space-between; align-items:center; border-bottom:1px solid #eee; padding:10px 0;}");
        sb.Append("</style></head><body>");

        sb.Append($"<h1>🕸️ Morpheo Node: {_options.NodeName}</h1>");

        sb.Append("<div class='card'>");
        sb.Append($"<h3>My Status</h3>");
        sb.Append($"<p><strong>Role:</strong> {_options.Role} | <strong>Port:</strong> {LocalPort}</p>");
        sb.Append("</div>");

        sb.Append("<div class='card'>");
        sb.Append($"<h3>Network Mesh ({peers.Count} peers)</h3>");
        sb.Append(peers.Count == 0 ? "<p><em>Waiting for neighbors...</em></p>" : "");

        foreach (var peer in peers)
        {
            sb.Append("<div class='peer-row'>");
            sb.Append($"<div><strong>{peer.Name}</strong> <br/><small>{peer.IpAddress}:{peer.Port}</small></div>");
            sb.Append("</div>");
        }
        sb.Append("</div>");
        sb.Append("<script>setTimeout(() => window.location.reload(), 3000);</script>");
        sb.Append("</body></html>");

        return sb.ToString();
    }

    public async Task StopAsync()
    {
        if (_app != null) await _app.StopAsync();
    }
}