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

    private readonly ConcurrentDictionary<string, DateTime> _heartbeats = new();

    public event EventHandler<PeerInfo>? PeerFound;
    public event EventHandler<PeerInfo>? PeerLost;

    public UdpDiscoveryService(MorpheoOptions options, ILogger<UdpDiscoveryService> logger)
    {
        _options = options;
        _logger = logger;
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
                if (packet.Name == _options.NodeName) continue;

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
                    Port = myInfo.Port, // <--- AJOUT CRUCIAL : On diffuse notre port HTTP
                    Type = DiscoveryMessageType.Hello
                };

                var data = DiscoveryPacket.Serialize(packet);
                await _udpClient!.SendAsync(data, data.Length, endpoint);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Erreur Broadcast : {ex.Message}");
            }

            await Task.Delay(3000, token);
        }
    }

    private async Task CleanupLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var timeout = TimeSpan.FromSeconds(10);

            foreach (var peer in _heartbeats)
            {
                if (now - peer.Value > timeout)
                {
                    if (_heartbeats.TryRemove(peer.Key, out _))
                    {
                        // Note: On met 0 pour le port ici car l'info n'est plus pertinente
                        PeerLost?.Invoke(this, new PeerInfo(peer.Key, "Unknown", "", 0, NodeRole.StandardClient));
                    }
                }
            }

            await Task.Delay(5000, token);
        }
    }

    private void HandleIncomingPacket(DiscoveryPacket packet, string realIp)
    {
        bool isNew = !_heartbeats.ContainsKey(packet.Id);
        _heartbeats[packet.Id] = DateTime.UtcNow;

        if (packet.Type == DiscoveryMessageType.Bye)
        {
            _heartbeats.TryRemove(packet.Id, out _);
            // On met packet.Port ici
            PeerLost?.Invoke(this, new PeerInfo(packet.Id, packet.Name, realIp, packet.Port, packet.Role));
            return;
        }

        if (isNew)
        {
            // <--- AJOUT CRUCIAL : On reconstruit le PeerInfo AVEC LE PORT reçu
            PeerFound?.Invoke(this, new PeerInfo(packet.Id, packet.Name, realIp, packet.Port, packet.Role));
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _udpClient?.Dispose();
    }
}