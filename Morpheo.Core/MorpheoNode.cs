using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;
using Morpheo.Core.Data;
using Morpheo.Core.Server; // <--- Ne pas oublier

namespace Morpheo.Core;

public class MorpheoNode : IMorpheoNode
{
    private readonly MorpheoOptions _options;
    private readonly INetworkDiscovery _discovery;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MorpheoNode> _logger;

    // Notre nouveau serveur web
    private readonly MorpheoWebServer _webServer;

    public MorpheoNode(
        MorpheoOptions options,
        INetworkDiscovery discovery,
        IServiceProvider serviceProvider,
        ILogger<MorpheoNode> logger,
        ILoggerFactory loggerFactory) // <--- On demande l'usine à logs
    {
        _options = options;
        _discovery = discovery;
        _serviceProvider = serviceProvider;
        _logger = logger;

        // On crée le serveur web manuellement
        _webServer = new MorpheoWebServer(options, loggerFactory.CreateLogger<MorpheoWebServer>());
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation($"🚀 Démarrage de Morpheo Node : {_options.NodeName}");

        // --- 1. Base de Données ---
        try
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
                var dbContext = scope.ServiceProvider.GetServices<MorpheoDbContext>().FirstOrDefault()
                                ?? throw new Exception("Aucun DbContext Morpheo n'a été enregistré !");

                await initializer.InitializeAsync(dbContext);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "❌ Impossible d'initialiser la base de données.");
            throw;
        }

        // --- 2. Serveur Web ---
        // On démarre le serveur web AVANT de s'annoncer sur le réseau
        await _webServer.StartAsync(ct);
        int myHttpPort = _webServer.LocalPort;

        // --- 3. Découverte Réseau ---
        // ASTUCE : On concatène le port dans le champ "IpAddress" pour l'instant (ex: "192.168.1.15:5123")
        // Cela permet aux voisins de savoir sur quel port HTTP nous contacter.
        // Dans une version future, on ajoutera un champ "Port" proprement dans le DiscoveryPacket.
        string myAddressIdentity = $"HTTP_PORT:{myHttpPort}";

        var myIdentity = new PeerInfo(Guid.NewGuid().ToString(), _options.NodeName, myAddressIdentity, _options.Role);

        _discovery.PeerFound += (s, peer) => _logger.LogInformation($"✨ Voisin trouvé : {peer.Name} -> {peer.IpAddress}");
        _discovery.PeerLost += (s, peer) => _logger.LogWarning($"💀 Voisin perdu : {peer.Name}");

        await _discovery.StartAdvertisingAsync(myIdentity, ct);

        _logger.LogInformation("✅ Morpheo est opérationnel (UDP + HTTP).");
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("Arrêt du nœud Morpheo.");
        await _webServer.StopAsync();
    }

    public INetworkDiscovery Discovery => _discovery;
}