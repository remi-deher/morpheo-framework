using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;
using System.Text;

namespace Morpheo.Core.Server;

public record PrintRequest(string Content, string Sender);

public class MorpheoWebServer
{
    private readonly MorpheoOptions _options;
    private readonly ILogger<MorpheoWebServer> _logger;
    private readonly INetworkDiscovery _discovery; // <--- On a besoin de ça pour lister les voisins
    private WebApplication? _app;

    public int LocalPort { get; private set; }

    public MorpheoWebServer(
        MorpheoOptions options,
        ILogger<MorpheoWebServer> logger,
        INetworkDiscovery discovery)
    {
        _options = options;
        _logger = logger;
        _discovery = discovery;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(0));

        _app = builder.Build();

        // --- API ---
        _app.MapGet("/api/ping", () => Results.Ok($"Pong from {_options.NodeName}"));
        _app.MapPost("/api/print", (PrintRequest request) =>
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"🖨️ [ORDRE REÇU de {request.Sender}] : \"{request.Content}\"");
            Console.ResetColor();
            return Results.Ok(new { status = "Printed" });
        });

        // --- DASHBOARD ---
        _app.MapGet("/morpheo/dashboard", () => Results.Content(GenerateDashboardHtml(), "text/html"));

        await _app.StartAsync(ct);
        LocalPort = _app.Urls.Select(u => new Uri(u).Port).FirstOrDefault();
        _logger.LogInformation($"🌍 Dashboard accessible sur : http://localhost:{LocalPort}/morpheo/dashboard");
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

        // Carte d'identité
        sb.Append("<div class='card'>");
        sb.Append($"<h3>My Status</h3>");
        sb.Append($"<p><strong>Role:</strong> {_options.Role} | <strong>Port:</strong> {LocalPort}</p>");
        sb.Append("<div><strong>My Capabilities:</strong><br/>");
        if (_options.Capabilities.Count == 0) sb.Append("<em>None</em>");
        foreach (var cap in _options.Capabilities) sb.Append($"<span class='badge'>{cap}</span>");
        sb.Append("</div></div>");

        // Liste des voisins
        sb.Append("<div class='card'>");
        sb.Append($"<h3>Network Mesh ({peers.Count} peers)</h3>");
        if (peers.Count == 0) sb.Append("<p><em>Waiting for neighbors...</em></p>");

        foreach (var peer in peers)
        {
            sb.Append("<div class='peer-row'>");
            sb.Append($"<div><strong>{peer.Name}</strong> <br/><small>{peer.IpAddress}:{peer.Port}</small></div>");
            sb.Append("<div>");
            foreach (var tag in peer.Tags) sb.Append($"<span class='badge'>{tag}</span>");
            sb.Append("</div></div>");
        }
        sb.Append("</div>");

        sb.Append("<script>setTimeout(() => window.location.reload(), 3000);</script>"); // Auto-refresh 3s
        sb.Append("</body></html>");

        return sb.ToString();
    }

    public async Task StopAsync()
    {
        if (_app != null) await _app.StopAsync();
    }
}