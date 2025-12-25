using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;

namespace Morpheo.Core.Discovery;

public class UdpDiscoveryService : INetworkDiscovery, IDisposable
{
    private readonly MorpheoOptions _options;
    private readonly ILogger<UdpDiscoveryService> _logger;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;

    // Stockage des pairs avec leur date de dernière vue pour le nettoyage (Timeout)
    private readonly ConcurrentDictionary<string, (PeerInfo Info, DateTime LastSeen)> _peers = new();

    public event EventHandler<PeerInfo>? PeerFound;
    public event EventHandler<PeerInfo>? PeerLost;

    public UdpDiscoveryService(MorpheoOptions options, ILogger<UdpDiscoveryService> logger)
    {
        _options = options;
        _logger = logger;
    }

    // Retourne la liste actuelle des voisins (utilisé par le Dashboard ou le Routing)
    public IReadOnlyList<PeerInfo> GetPeers()
    {
        return _peers.Values.Select(x => x.Info).ToList();
    }

    public async Task StartAdvertisingAsync(PeerInfo myInfo, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            _udpClient = new UdpClient();
            // Permet à plusieurs instances de tourner sur la même machine (utile pour les tests)
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _options.DiscoveryPort));
            _udpClient.EnableBroadcast = true;

            _logger.LogInformation($"📡 Discovery actif sur le port {_options.DiscoveryPort}");

            var tasks = new List<Task>
            {
                ReceiveLoopAsync(_cts.Token),
                BroadcastLoopAsync(myInfo, _cts.Token),
                CleanupLoopAsync(_cts.Token)
            };

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur critique dans le service Discovery");
            throw; // Mieux vaut crasher au démarrage que de tourner sans réseau
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient!.ReceiveAsync(token);
                var packet = DiscoveryPacket.Deserialize(result.Buffer);

                if (packet == null) continue;
                if (packet.Name == _options.NodeName) continue; // On s'ignore soi-même

                HandleIncomingPacket(packet, result.RemoteEndPoint.Address.ToString());
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning($"Paquet malformé reçu : {ex.Message}");
            }
        }
    }

    private async Task BroadcastLoopAsync(PeerInfo myInfo, CancellationToken token)
    {
        var endpoint = new IPEndPoint(IPAddress.Broadcast, _options.DiscoveryPort);

        while (!token.IsCancellationRequested)
        {
            try
            {
                var packet = new DiscoveryPacket
                {
                    Id = myInfo.Id,
                    Name = myInfo.Name,
                    Role = myInfo.Role,
                    IpAddress = myInfo.IpAddress,
                    Port = myInfo.Port,
                    Tags = myInfo.Tags,
                    Type = DiscoveryMessageType.Hello
                };

                var data = DiscoveryPacket.Serialize(packet);
                await _udpClient!.SendAsync(data, data.Length, endpoint);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Erreur Broadcast : {ex.Message}");
            }

            // MODIFICATION CONFIGURABLE :
            // On utilise l'intervalle défini dans les options (par défaut 3s)
            // au lieu d'une valeur codée en dur.
            await Task.Delay(_options.DiscoveryInterval, token);
        }
    }

    private async Task CleanupLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;

            // On considère un nœud perdu s'il n'a pas donné signe de vie depuis 3x l'intervalle d'annonce + 1s de marge
            // Ex: Si intervalle = 3s -> Timeout = 10s
            var timeout = _options.DiscoveryInterval * 3 + TimeSpan.FromSeconds(1);

            foreach (var peer in _peers)
            {
                if (now - peer.Value.LastSeen > timeout)
                {
                    if (_peers.TryRemove(peer.Key, out var removed))
                    {
                        _logger.LogInformation($"❌ Voisin perdu (Timeout) : {removed.Info.Name}");
                        PeerLost?.Invoke(this, removed.Info);
                    }
                }
            }

            await Task.Delay(5000, token); // Vérification toutes les 5s
        }
    }

    private void HandleIncomingPacket(DiscoveryPacket packet, string realIp)
    {
        // On reconstruit l'objet PeerInfo avec toutes les données reçues
        var info = new PeerInfo(packet.Id, packet.Name, realIp, packet.Port, packet.Role, packet.Tags);

        if (packet.Type == DiscoveryMessageType.Bye)
        {
            if (_peers.TryRemove(packet.Id, out _))
            {
                PeerLost?.Invoke(this, info);
            }
            return;
        }

        bool isNew = !_peers.ContainsKey(packet.Id);

        // Mise à jour du timestamp (Heartbeat)
        _peers[packet.Id] = (info, DateTime.UtcNow);

        if (isNew)
        {
            _logger.LogInformation($"✨ Voisin trouvé : {info.Name} ({info.IpAddress})");
            PeerFound?.Invoke(this, info);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _udpClient?.Dispose();
    }
}