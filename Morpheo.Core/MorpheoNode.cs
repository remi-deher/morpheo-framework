using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;

namespace Morpheo.Core;

public class MorpheoNode : IMorpheoNode
{
    private readonly MorpheoOptions _options;
    private readonly INetworkDiscovery _discovery;
    private readonly IPrintGateway _printGateway;
    private readonly ILogger<MorpheoNode> _logger;

    // L'injection de dépendances nous fournit les implémentations concrètes
    public MorpheoNode(
        MorpheoOptions options,
        INetworkDiscovery discovery,
        IPrintGateway printGateway,
        ILogger<MorpheoNode> logger)
    {
        _options = options;
        _discovery = discovery;
        _printGateway = printGateway;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation($"?? Démarrage de Morpheo Node : {_options.NodeName}");

        // 1. Initialiser la BDD locale (SQLite)
        // TODO: Init Database logic here...

        // 2. Lancer la découverte réseau
        var myIdentity = new PeerInfo(Guid.NewGuid().ToString(), _options.NodeName, "127.0.0.1", _options.Role);

        // On s'abonne aux événements
        _discovery.PeerFound += (s, peer) => _logger.LogInformation($"Voisin trouvé : {peer.Name}");

        await _discovery.StartAdvertisingAsync(myIdentity, ct);

        _logger.LogInformation("? Morpheo est opérationnel et en écoute.");
    }

    public async Task StopAsync()
    {
        // Logique de fermeture propre
        _logger.LogInformation("Arrêt du nœud Morpheo.");
    }
}