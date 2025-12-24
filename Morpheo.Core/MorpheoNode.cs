using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;
using Morpheo.Core.Data;
using Morpheo.Core.Server;

namespace Morpheo.Core;

public class MorpheoNode : IMorpheoNode
{
    private readonly MorpheoOptions _options;
    private readonly INetworkDiscovery _discovery;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MorpheoNode> _logger;
    private readonly MorpheoWebServer _webServer;

    private readonly IMorpheoClient _client;

    // On garde une trace de la tâche de fond pour info
    private Task? _discoveryTask;

    public MorpheoNode(
        MorpheoOptions options,
        INetworkDiscovery discovery,
        IServiceProvider serviceProvider,
        ILogger<MorpheoNode> logger,
        ILoggerFactory loggerFactory,
        IMorpheoClient client)
    {
        _options = options;
        _discovery = discovery;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _client = client;

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
                var dbContext = scope.ServiceProvider.GetServices<MorpheoDbContext>().FirstOrDefault();

                if (dbContext == null)
                    throw new Exception("Aucun DbContext Morpheo n'a été enregistré !");

                await initializer.InitializeAsync(dbContext);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "❌ Impossible d'initialiser la base de données.");
            throw;
        }

        // --- 2. Serveur Web ---
        await _webServer.StartAsync(ct);
        int myHttpPort = _webServer.LocalPort;

        // --- 3. Découverte Réseau ---
        var myIdentity = new PeerInfo(
            Guid.NewGuid().ToString(),
            _options.NodeName,
            "IP_AUTO",
            myHttpPort,
            _options.Role
        );

        _discovery.PeerFound += (s, peer) => _logger.LogInformation($"✨ Voisin trouvé : {peer.Name} ({peer.IpAddress}:{peer.Port})");
        _discovery.PeerLost += (s, peer) => _logger.LogWarning($"💀 Voisin perdu : {peer.Name}");

        // CORRECTION CRUCIALE ICI :
        // On ne fait PLUS 'await' car cela bloque tout le programme.
        // On lance la tâche en parallèle et on laisse le programme continuer.
        _discoveryTask = _discovery.StartAdvertisingAsync(myIdentity, ct);

        _logger.LogInformation("✅ Morpheo est opérationnel (UDP + HTTP + CLIENT).");
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("Arrêt du nœud Morpheo.");
        await _webServer.StopAsync();
        // La tâche _discoveryTask s'arrêtera quand le CancellationToken sera annulé ou l'objet disposé
    }

    public INetworkDiscovery Discovery => _discovery;
    public IMorpheoClient Client => _client;
}