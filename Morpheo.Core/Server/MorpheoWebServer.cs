using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;

namespace Morpheo.Core.Server;

public class MorpheoWebServer
{
    private readonly MorpheoOptions _options;
    private readonly ILogger<MorpheoWebServer> _logger;
    private WebApplication? _app;

    // Le port est public pour que le Node puisse le lire et l'annoncer aux autres
    public int LocalPort { get; private set; }

    public MorpheoWebServer(MorpheoOptions options, ILogger<MorpheoWebServer> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();

        // On nettoie les logs par défaut d'ASP.NET pour ne pas polluer la console
        builder.Logging.ClearProviders();

        // On configure Kestrel (le moteur web)
        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            // Port 0 = Demander au système d'attribuer un port libre aléatoire automatiquement
            serverOptions.ListenAnyIP(0);
        });

        _app = builder.Build();

        // --- NOS API ---

        // 1. Endpoint de santé (Ping) - Utile pour vérifier que le voisin est vraiment là
        _app.MapGet("/api/ping", () => Results.Ok($"Pong from {_options.NodeName}"));

        // 2. Endpoint de test
        _app.MapGet("/api/info", () => Results.Json(new
        {
            Name = _options.NodeName,
            Role = _options.Role.ToString(),
            Time = DateTime.UtcNow
        }));

        await _app.StartAsync(ct);

        // On récupère le port que Windows/Linux nous a réellement attribué
        LocalPort = _app.Urls.Select(u => new Uri(u).Port).FirstOrDefault();

        _logger.LogInformation($"🌍 Serveur HTTP Morpheo démarré sur le port : {LocalPort}");
    }

    public async Task StopAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}