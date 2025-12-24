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

    // MODIFICATION MAJEURE : On stocke l'info complète du pair + la date de dernière vue
    // Cela permet au Dashboard d'afficher les noms et les tags des voisins
    private readonly ConcurrentDictionary<string, (PeerInfo Info, DateTime LastSeen)> _peers = new();

    public event EventHandler<PeerInfo>? PeerFound;
    public event EventHandler<PeerInfo>? PeerLost;

    public UdpDiscoveryService(MorpheoOptions options, ILogger<UdpDiscoveryService> logger)
    {
        _options = options;
        _logger = logger;
    }

    // Implémentation de la méthode requise par le Dashboard
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
            throw;
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
                    Tags = myInfo.Tags, // <--- IMPORTANT : On diffuse nos capacités
                    Type = DiscoveryMessageType.Hello
                };

                var data = DiscoveryPacket.Serialize(packet);
                await _udpClient!.SendAsync(data, data.Length, endpoint);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Erreur Broadcast : {ex.Message}");
            }

            await Task.Delay(3000, token); // Hello toutes les 3 secondes
        }
    }

    private async Task CleanupLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var timeout = TimeSpan.FromSeconds(10); // Timeout après 10s sans nouvelles

            foreach (var peer in _peers)
            {
                if (now - peer.Value.LastSeen > timeout)
                {
                    if (_peers.TryRemove(peer.Key, out var removed))
                    {
                        PeerLost?.Invoke(this, removed.Info);
                    }
                }
            }

            await Task.Delay(5000, token);
        }
    }

    private void HandleIncomingPacket(DiscoveryPacket packet, string realIp)
    {
        // On reconstruit l'objet PeerInfo avec toutes les données reçues (Tags inclus)
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

        // On met à jour ou on ajoute le pair
        _peers[packet.Id] = (info, DateTime.UtcNow);

        if (isNew)
        {
            PeerFound?.Invoke(this, info);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _udpClient?.Dispose();
    }
}