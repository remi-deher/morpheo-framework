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
        IServiceProvider serviceProvider, // 👈 On récupère le container principal ici
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

        // 🔧 FIX : On passe 'serviceProvider' comme 5ème argument
        _webServer = new MorpheoWebServer(
            options,
            loggerFactory.CreateLogger<MorpheoWebServer>(),
            discovery,
            syncService,
            serviceProvider // <--- C'est ici qu'on fait le lien !
        );
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation($"🚀 Démarrage de Morpheo Node : {_options.NodeName}");

        // 1. Initialisation de la BDD via le Scope principal
        try
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
                // On récupère tous les DbContexts enregistrés pour trouver celui de Morpheo
                var dbContext = scope.ServiceProvider.GetServices<MorpheoDbContext>().FirstOrDefault();

                if (dbContext == null)
                    throw new Exception("Aucun DbContext Morpheo n'a été enregistré !");

                await initializer.InitializeAsync(dbContext);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "❌ Erreur critique lors de l'initialisation BDD.");
            throw;
        }

        // 2. Démarrage du Web Server (qui utilisera aussi _serviceProvider pour ses scopes)
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
        catch { /* Ignoré si erreur imprimante */ }

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
    public DataSyncService Sync => _syncService;
}