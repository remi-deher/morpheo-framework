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

    // Mémoire vive des voisins : <PeerId, LastSeenTime>
    private readonly ConcurrentDictionary<string, DateTime> _heartbeats = new();

    public event EventHandler<PeerInfo>? PeerFound;
    public event EventHandler<PeerInfo>? PeerLost;

    public UdpDiscoveryService(MorpheoOptions options, ILogger<UdpDiscoveryService> logger)
    {
        _options = options;
        _logger = logger;

        // On valide la configuration dès le début pour éviter les erreurs silencieuses
        _options.Validate();
    }

    public async Task StartAdvertisingAsync(PeerInfo myInfo, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            // 1. Initialisation du Socket UDP
            // On écoute sur toutes les interfaces (0.0.0.0) au port défini
            _udpClient = new UdpClient();
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            // Utilisation de la variable configurée par le développeur
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _options.DiscoveryPort));

            // Pour Android/Linux, parfois nécessaire pour le broadcast
            _udpClient.EnableBroadcast = true;

            _logger.LogInformation($"📡 Discovery actif sur le port {_options.DiscoveryPort}");

            // 2. Lancer les 3 tâches parallèles
            var tasks = new List<Task>
            {
                ReceiveLoopAsync(_cts.Token),              // Listen
                BroadcastLoopAsync(myInfo, _cts.Token),    // Hearthbeat
                CleanupLoopAsync(_cts.Token)               // Cleanup
            };

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur critique dans le service Discovery");
            throw;
        }
    }

    // --- Tâche 1 : Écouter les autres ---
    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient!.ReceiveAsync(token);
                var packet = DiscoveryPacket.Deserialize(result.Buffer);

                if (packet == null) continue;

                // On ignore nos propres messages (Echo)
                // Note : Comparaison simplifiée, idéalement comparer l'ID unique
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

    // --- Tâche 2 : Boradcast que nous sommes disponible ---
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
                    IpAddress = myInfo.IpAddress, // Note: Sera souvent remplacé par l'IP réelle vue par le récepteur
                    Type = DiscoveryMessageType.Hello
                };

                var data = DiscoveryPacket.Serialize(packet);
                await _udpClient!.SendAsync(data, data.Length, endpoint);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Erreur Broadcast (peut être normal au démarrage) : {ex.Message}");
            }

            // Attendre 3 secondes avant le prochain heartbeat
            await Task.Delay(3000, token);
        }
    }

    // --- Tâche 3 : Vérifier qui est mort ---
    private async Task CleanupLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var timeout = TimeSpan.FromSeconds(10); // Si pas de nouvelles en 10s -> mort

            foreach (var peer in _heartbeats)
            {
                if (now - peer.Value > timeout)
                {
                    // Le voisin est muet depuis trop longtemps
                    if (_heartbeats.TryRemove(peer.Key, out _))
                    {
                        _logger.LogWarning($"💀 Voisin perdu (Timeout) : {peer.Key}");
                        PeerLost?.Invoke(this, new PeerInfo(peer.Key, "Unknown", "", NodeRole.StandardClient));
                    }
                }
            }

            await Task.Delay(5000, token); // Vérification toutes les 5 secondes
        }
    }

    private void HandleIncomingPacket(DiscoveryPacket packet, string realIp)
    {
        // Mise à jour du timestamp de "Last seen"
        bool isNew = !_heartbeats.ContainsKey(packet.Id);
        _heartbeats[packet.Id] = DateTime.UtcNow;

        if (packet.Type == DiscoveryMessageType.Bye)
        {
            _heartbeats.TryRemove(packet.Id, out _);
            PeerLost?.Invoke(this, new PeerInfo(packet.Id, packet.Name, realIp, packet.Role));
            return;
        }

        if (isNew)
        {
            _logger.LogInformation($"✨ Nouveau voisin détecté : {packet.Name} ({realIp})");
            PeerFound?.Invoke(this, new PeerInfo(packet.Id, packet.Name, realIp, packet.Role));
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _udpClient?.Dispose();
    }
}