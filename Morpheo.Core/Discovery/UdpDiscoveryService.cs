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

    // Tâches de fond pour garder une trace
    private Task? _receiveTask;
    private Task? _broadcastTask;
    private Task? _cleanupTask;

    private readonly ConcurrentDictionary<string, (PeerInfo Info, DateTime LastSeen)> _peers = new();

    public event EventHandler<PeerInfo>? PeerFound;
    public event EventHandler<PeerInfo>? PeerLost;

    public UdpDiscoveryService(MorpheoOptions options, ILogger<UdpDiscoveryService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public IReadOnlyList<PeerInfo> GetPeers()
    {
        return _peers.Values.Select(x => x.Info).ToList();
    }

    // Initialisation unique du Socket
    private void EnsureSocketInitialized()
    {
        if (_udpClient != null) return;

        _udpClient = new UdpClient();
        _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _options.DiscoveryPort));
        _udpClient.EnableBroadcast = true;

        _cts = new CancellationTokenSource();

        _logger.LogInformation($"📡 Socket UDP ouvert sur le port {_options.DiscoveryPort}");
    }

    public Task StartAdvertisingAsync(PeerInfo myInfo, CancellationToken ct)
    {
        EnsureSocketInitialized();

        // On lie le token reçu avec notre CTS interne pour pouvoir arrêter via Stop()
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts!.Token, ct);

        // On lance la tâche en fond (Fire and Forget) sans l'attendre
        _broadcastTask = BroadcastLoopAsync(myInfo, linkedCts.Token);

        // On lance aussi le nettoyage ici, c'est un bon endroit
        _cleanupTask = CleanupLoopAsync(linkedCts.Token);

        return Task.CompletedTask;
    }

    public Task StartListeningAsync(CancellationToken ct)
    {
        EnsureSocketInitialized();

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts!.Token, ct);

        // On lance l'écoute en fond
        _receiveTask = ReceiveLoopAsync(linkedCts.Token);

        return Task.CompletedTask;
    }

    public void Stop()
    {
        _logger.LogInformation("Arrêt du service Discovery...");
        _cts?.Cancel(); // Annule toutes les boucles
        _udpClient?.Close();
        _udpClient?.Dispose();
        _udpClient = null;
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        _logger.LogDebug("👂 Début écoute UDP");
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_udpClient == null) break;

                // ReceiveAsync n'accepte pas de token directement avant .NET 6/7, 
                // mais on peut utiliser WaitAsync ou juste catcher l'erreur à la fermeture.
                var result = await _udpClient.ReceiveAsync(token);
                var packet = DiscoveryPacket.Deserialize(result.Buffer);

                if (packet == null) continue;
                if (packet.Name == _options.NodeName) continue;

                HandleIncomingPacket(packet, result.RemoteEndPoint.Address.ToString());
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning($"Paquet UDP ignoré : {ex.Message}");
            }
        }
    }

    private async Task BroadcastLoopAsync(PeerInfo myInfo, CancellationToken token)
    {
        _logger.LogDebug("📢 Début broadcast UDP");
        var endpoint = new IPEndPoint(IPAddress.Broadcast, _options.DiscoveryPort);

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_udpClient == null) break;

                var packet = new DiscoveryPacket
                {
                    Id = myInfo.Id,
                    Name = myInfo.Name,
                    Role = myInfo.Role,
                    IpAddress = myInfo.IpAddress, // Sera souvent "0.0.0.0", le récepteur déduira l'IP réelle
                    Port = myInfo.Port,
                    Tags = myInfo.Tags,
                    Type = DiscoveryMessageType.Hello
                };

                var data = DiscoveryPacket.Serialize(packet);
                await _udpClient.SendAsync(data, data.Length, endpoint);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Erreur Broadcast : {ex.Message}");
            }

            try { await Task.Delay(_options.DiscoveryInterval, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task CleanupLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var timeout = _options.DiscoveryInterval * 3 + TimeSpan.FromSeconds(2);

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

            try { await Task.Delay(5000, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void HandleIncomingPacket(DiscoveryPacket packet, string realIp)
    {
        // On utilise l'IP détectée par le socket (realIp) car packet.IpAddress est souvent générique
        var info = new PeerInfo(packet.Id, packet.Name, realIp, packet.Port, packet.Role, packet.Tags);

        if (packet.Type == DiscoveryMessageType.Bye)
        {
            if (_peers.TryRemove(packet.Id, out _)) PeerLost?.Invoke(this, info);
            return;
        }

        bool isNew = !_peers.ContainsKey(packet.Id);
        _peers[packet.Id] = (info, DateTime.UtcNow);

        if (isNew)
        {
            _logger.LogInformation($"✨ Voisin trouvé : {info.Name} @ {realIp}:{info.Port}");
            PeerFound?.Invoke(this, info);
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}