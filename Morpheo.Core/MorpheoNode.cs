using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;
using Morpheo.Core.Discovery;
using Morpheo.Core.Server;
using Morpheo.Core.Sync;

namespace Morpheo.Core;

public class MorpheoNode : IHostedService // On le rend compatible avec le Generic Host
{
    private readonly MorpheoOptions _options;
    private readonly INetworkDiscovery _discovery;
    private readonly MorpheoWebServer _webServer;
    private readonly DataSyncService _syncService; // Gardé pour référence si besoin
    private readonly ILogger<MorpheoNode> _logger;

    // CORRECTION ICI : On injecte le MorpheoWebServer déjà construit par le conteneur DI.
    // Plus besoin de faire "new MorpheoWebServer(...)" manuellement.
    public MorpheoNode(
        MorpheoOptions options,
        INetworkDiscovery discovery,
        MorpheoWebServer webServer,
        DataSyncService syncService,
        ILogger<MorpheoNode> logger)
    {
        _options = options;
        _discovery = discovery;
        _webServer = webServer;
        _syncService = syncService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation($"🚀 Démarrage du Nœud Morpheo : {_options.NodeName}");

        // 1. Démarrage du Serveur Web (API + WebSocket)
        await _webServer.StartAsync(cancellationToken);
        _logger.LogInformation($"🌐 API Morpheo écoute sur le port {_webServer.LocalPort}");

        // 2. Démarrage de la Découverte (UDP Broadcast)
        // On annonce notre présence sur le réseau
        var myInfo = new PeerInfo(
            Id: _options.NodeName, // Ou un GUID stable
            Name: _options.NodeName,
            IpAddress: "0.0.0.0", // Sera résolu dynamiquement par les pairs
            Port: _webServer.LocalPort,
            Role: _options.Role,
            Tags: _options.Capabilities.ToArray()
        );

        await _discovery.StartAdvertisingAsync(myInfo, cancellationToken);
        await _discovery.StartListeningAsync(cancellationToken);

        _logger.LogInformation("📡 Service de découverte actif.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🛑 Arrêt du Nœud Morpheo...");
        await _webServer.StopAsync();
        _discovery.Stop();
    }
}