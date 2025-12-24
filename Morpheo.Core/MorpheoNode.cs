using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;
using Morpheo.Core.Client; // Pour IMorpheoClient
using Morpheo.Core.Data;   // Pour DatabaseInitializer
using Morpheo.Core.Printers; // Pour WindowsPrinterService
using Morpheo.Core.Server; // Pour MorpheoWebServer

namespace Morpheo.Core;

public class MorpheoNode : IMorpheoNode
{
    private readonly MorpheoOptions _options;
    private readonly INetworkDiscovery _discovery;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MorpheoNode> _logger;
    private readonly MorpheoWebServer _webServer;
    private readonly IMorpheoClient _client;

    // NOUVEAU : Le service qui scanne les imprimantes Windows
    private readonly WindowsPrinterService _printerService;

    // On garde une trace de la tâche de fond pour info
    private Task? _discoveryTask;

    public MorpheoNode(
        MorpheoOptions options,
        INetworkDiscovery discovery,
        IServiceProvider serviceProvider,
        ILogger<MorpheoNode> logger,
        ILoggerFactory loggerFactory,
        IMorpheoClient client,
        WindowsPrinterService printerService) // <--- Injection ici
    {
        _options = options;
        _discovery = discovery;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _client = client;
        _printerService = printerService; // <--- On stocke le service

        // On passe 'discovery' au constructeur du WebServer pour le Dashboard
        _webServer = new MorpheoWebServer(options, loggerFactory.CreateLogger<MorpheoWebServer>(), discovery);
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

        // --- 3. Découverte Réseau & Capacités ---

        // A. On prépare la liste des capacités (Capabilities)
        var finalCapabilities = new List<string>(_options.Capabilities);

        // B. On scanne les imprimantes réelles et on les ajoute
        try
        {
            _logger.LogInformation("🔍 Scan des imprimantes locales...");
            var localPrinters = _printerService.GetAvailablePrinters();
            foreach (var p in localPrinters)
            {
                // Format du Tag : "PRINTER:{Groupe}:{Nom}"
                // Exemple : "PRINTER:KITCHEN:Zebra_GK420t"
                string tag = $"PRINTER:{p.Group}:{p.Name}";
                finalCapabilities.Add(tag);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚠️ Erreur lors du scan des imprimantes (Fonctionnalité désactivée).");
        }

        // C. On construit l'identité avec la liste complète
        var myIdentity = new PeerInfo(
            Guid.NewGuid().ToString(),
            _options.NodeName,
            "IP_AUTO",
            myHttpPort,
            _options.Role,
            finalCapabilities.ToArray() // <--- Liste fusionnée (Config + Auto-Scan)
        );

        _discovery.PeerFound += (s, peer) => _logger.LogInformation($"✨ Voisin trouvé : {peer.Name} ({peer.IpAddress}:{peer.Port})");
        _discovery.PeerLost += (s, peer) => _logger.LogWarning($"💀 Voisin perdu : {peer.Name}");

        // D. Lancement en tâche de fond (Non-bloquant)
        _discoveryTask = _discovery.StartAdvertisingAsync(myIdentity, ct);

        _logger.LogInformation("✅ Morpheo est opérationnel (UDP + HTTP + CLIENT + PRINTERS).");
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("Arrêt du nœud Morpheo.");
        await _webServer.StopAsync();
    }

    public INetworkDiscovery Discovery => _discovery;
    public IMorpheoClient Client => _client;
}