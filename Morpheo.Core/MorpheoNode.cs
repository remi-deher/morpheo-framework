using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;
using Morpheo.Core.Client;
using Morpheo.Core.Data;
using Morpheo.Core.Printers;
using Morpheo.Core.Server;
using Morpheo.Core.Sync;

namespace Morpheo.Core;

public class MorpheoNode : IMorpheoNode
{
    private readonly MorpheoOptions _options;
    private readonly INetworkDiscovery _discovery;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MorpheoNode> _logger;
    private readonly MorpheoWebServer _webServer;
    private readonly IMorpheoClient _client;
    private readonly WindowsPrinterService _printerService;
    private readonly DataSyncService _syncService;

    private Task? _discoveryTask;

    public MorpheoNode(
        MorpheoOptions options,
        INetworkDiscovery discovery,
        IServiceProvider serviceProvider, // 👈 On récupère le container principal
        ILogger<MorpheoNode> logger,
        ILoggerFactory loggerFactory,
        IMorpheoClient client,
        WindowsPrinterService printerService,
        DataSyncService syncService)
    {
        _options = options;
        _discovery = discovery;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _client = client;
        _printerService = printerService;
        _syncService = syncService;

        // ✅ C'est le bon endroit pour créer le WebServer
        // On passe 'serviceProvider' pour qu'il puisse accéder à la BDD principale
        _webServer = new MorpheoWebServer(
            options,
            loggerFactory.CreateLogger<MorpheoWebServer>(),
            discovery,
            syncService,
            serviceProvider // <--- LA CLEF DU FIX 500
        );
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation($"🚀 Démarrage de Morpheo Node : {_options.NodeName}");

        // 1. Init BDD
        using (var scope = _serviceProvider.CreateScope())
        {
            var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
            var dbContext = scope.ServiceProvider.GetServices<MorpheoDbContext>().FirstOrDefault();
            if (dbContext == null) throw new Exception("Erreur config BDD");
            await initializer.InitializeAsync(dbContext);
        }

        // 2. Start Web Server
        await _webServer.StartAsync(ct);

        // 3. Discovery
        var port = _webServer.LocalPort;
        var identity = new PeerInfo(Guid.NewGuid().ToString(), _options.NodeName, "IP_AUTO", port, _options.Role, Array.Empty<string>());

        _discovery.PeerFound += (s, p) => _logger.LogInformation($"✨ Voisin : {p.Name}");
        _discoveryTask = _discovery.StartAdvertisingAsync(identity, ct);

        _logger.LogInformation("✅ Node Prêt.");
    }

    public async Task StopAsync()
    {
        await _webServer.StopAsync();
    }

    public INetworkDiscovery Discovery => _discovery;
    public IMorpheoClient Client => _client;
    public DataSyncService Sync => _syncService;
}