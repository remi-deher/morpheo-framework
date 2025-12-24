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
    private readonly DataSyncService _syncService; // <--- Le service de synchro

    private Task? _discoveryTask;

    public MorpheoNode(
        MorpheoOptions options,
        INetworkDiscovery discovery,
        IServiceProvider serviceProvider,
        ILogger<MorpheoNode> logger,
        ILoggerFactory loggerFactory,
        IMorpheoClient client,
        WindowsPrinterService printerService,
        DataSyncService syncService) // <--- Injection de syncService
    {
        _options = options;
        _discovery = discovery;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _client = client;
        _printerService = printerService;
        _syncService = syncService;

        // On passe syncService au WebServer
        _webServer = new MorpheoWebServer(
            options,
            loggerFactory.CreateLogger<MorpheoWebServer>(),
            discovery,
            syncService
        );
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation($"🚀 Démarrage de Morpheo Node : {_options.NodeName}");

        // 1. BDD
        try
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
                var dbContext = scope.ServiceProvider.GetServices<MorpheoDbContext>().FirstOrDefault();
                if (dbContext == null) throw new Exception("Aucun DbContext Morpheo n'a été enregistré !");
                await initializer.InitializeAsync(dbContext);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "❌ Erreur BDD.");
            throw;
        }

        // 2. Web Server
        await _webServer.StartAsync(ct);
        int myHttpPort = _webServer.LocalPort;

        // 3. Découverte & Printers
        var finalCapabilities = new List<string>(_options.Capabilities);
        try
        {
            var localPrinters = _printerService.GetAvailablePrinters();
            foreach (var p in localPrinters)
            {
                finalCapabilities.Add($"PRINTER:{p.Group}:{p.Name}");
            }
        }
        catch { /* Ignoré */ }

        var myIdentity = new PeerInfo(
            Guid.NewGuid().ToString(),
            _options.NodeName,
            "IP_AUTO",
            myHttpPort,
            _options.Role,
            finalCapabilities.ToArray()
        );

        _discovery.PeerFound += (s, peer) => _logger.LogInformation($"✨ Voisin trouvé : {peer.Name}");
        _discovery.PeerLost += (s, peer) => _logger.LogWarning($"💀 Voisin perdu : {peer.Name}");

        _discoveryTask = _discovery.StartAdvertisingAsync(myIdentity, ct);

        _logger.LogInformation("✅ Morpheo Node opérationnel.");
    }

    public async Task StopAsync()
    {
        await _webServer.StopAsync();
    }

    public INetworkDiscovery Discovery => _discovery;
    public IMorpheoClient Client => _client;
    public DataSyncService Sync => _syncService; // <--- Exposition du service
}