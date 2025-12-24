using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection; // Important pour IServiceProvider
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;
using Morpheo.Core.Data;

namespace Morpheo.Core;

public class MorpheoNode : IMorpheoNode
{
    private readonly MorpheoOptions _options;
    private readonly INetworkDiscovery _discovery;
    private readonly IServiceProvider _serviceProvider; // Pour récupérer la BDD dynamiquement
    private readonly ILogger<MorpheoNode> _logger;

    public MorpheoNode(
        MorpheoOptions options,
        INetworkDiscovery discovery,
        IServiceProvider serviceProvider,
        ILogger<MorpheoNode> logger)
    {
        _options = options;
        _discovery = discovery;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation($"🚀 Démarrage de Morpheo Node : {_options.NodeName}");

        // --- ÉTAPE 1 : Initialisation de la Base de Données ---
        try
        {
            // On crée un scope car le DbContext est souvent "Scoped" (durée de vie courte)
            using (var scope = _serviceProvider.CreateScope())
            {
                var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();

                // On récupère le DbContext générique (peu importe son type réel AppDbContext, etc.)
                // Note : On cherche n'importe quel contexte qui hérite de MorpheoDbContext
                var dbContext = scope.ServiceProvider.GetServices<MorpheoDbContext>().FirstOrDefault()
                                ?? throw new Exception("Aucun DbContext Morpheo n'a été enregistré !");

                await initializer.InitializeAsync(dbContext);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "❌ Impossible d'initialiser la base de données. Arrêt d'urgence.");
            throw; // Si pas de BDD, le système ne doit pas démarrer.
        }

        // --- ÉTAPE 2 : Lancer la découverte réseau ---
        var myIdentity = new PeerInfo(Guid.NewGuid().ToString(), _options.NodeName, "IP_AUTO", _options.Role);

        // Abonnement aux logs
        _discovery.PeerFound += (s, peer) => _logger.LogInformation($"✨ Voisin trouvé : {peer.Name} ({peer.IpAddress})");
        _discovery.PeerLost += (s, peer) => _logger.LogWarning($"💀 Voisin perdu : {peer.Name}");

        // Démarrage UDP
        await _discovery.StartAdvertisingAsync(myIdentity, ct);

        _logger.LogInformation("✅ Morpheo est opérationnel et en écoute.");
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("Arrêt du nœud Morpheo.");
        // Ici on pourrait ajouter la logique pour envoyer un message "Bye" en UDP
    }

    // Accesseurs pour les couches supérieures (WinUI/Android pourront s'en servir)
    public INetworkDiscovery Discovery => _discovery;
}