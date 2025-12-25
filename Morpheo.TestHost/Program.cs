using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;
using Morpheo.Core;
using Morpheo.Core.Data;
using Morpheo.Core.Sync; // Pour DataSyncService
using Morpheo.TestHost;

// Titre de la fenêtre
Console.Title = "Morpheo Test Node";

var builder = Host.CreateApplicationBuilder(args);

// Logs propres
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Configuration Morpheo
builder.Services.AddMorpheo<TestDbContext>(options =>
{
    options.NodeName = "CAISSE_" + Random.Shared.Next(100, 999);
    options.Role = NodeRole.StandardClient;
    options.DiscoveryPort = 5555;

    // Config imprimantes
    options.Printers
        .Exclude("Microsoft.*")
        .Exclude("Fax")
        .DefineGroup("KITCHEN", ".*Zebra.*");
});

var host = builder.Build();

// CORRECTION 1 : On récupère les services individuellement via l'injection
var node = host.Services.GetRequiredService<MorpheoNode>();
var discovery = host.Services.GetRequiredService<INetworkDiscovery>();
var sync = host.Services.GetRequiredService<DataSyncService>();
var client = host.Services.GetRequiredService<IMorpheoClient>();

Console.WriteLine("--- DÉMARRAGE DU TEST ---");
// CORRECTION 2 : On passe un CancellationToken
await node.StartAsync(CancellationToken.None);

// Gestion des voisins pour l'affichage (via le service Discovery injecté)
var neighbors = new List<PeerInfo>();
discovery.PeerFound += (s, peer) => { if (!neighbors.Any(n => n.Id == peer.Id)) neighbors.Add(peer); };
discovery.PeerLost += (s, peer) => { neighbors.RemoveAll(n => n.Id == peer.Id); };

Console.WriteLine("-------------------------------------------------");
Console.WriteLine(" [P] 'P' -> Test d'IMPRESSION (Hardware)");
Console.WriteLine(" [S] 'S' -> Test de SYNCHRONISATION (Data)");
Console.WriteLine(" [Q] 'Q' -> QUITTER");
Console.WriteLine("-------------------------------------------------");

while (true)
{
    if (Console.KeyAvailable)
    {
        var key = Console.ReadKey(true).Key;

        if (key == ConsoleKey.Q) break;

        // --- TEST IMPRESSION ---
        if (key == ConsoleKey.P)
        {
            if (neighbors.Count == 0) Console.WriteLine("⚠️ Pas de voisin.");
            else
            {
                var target = neighbors.First();
                Console.WriteLine($"\n📤 Envoi impression vers {target.Name}...");
                // Utilisation du client injecté
                await client.SendPrintJobAsync(target, "Ticket #1234 : 1x Café");
            }
        }

        // --- TEST SYNCHRO ---
        if (key == ConsoleKey.S)
        {
            var newProduct = new Product
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Produit Test " + Random.Shared.Next(1, 100),
                Price = 10.50m
            };

            Console.WriteLine($"\n🔄 Création locale de : {newProduct.Name}");
            // Utilisation du service Sync injecté
            await sync.BroadcastChangeAsync(newProduct, "CREATE");
        }
    }
    await Task.Delay(100);
}

// CORRECTION 3 : Stop avec CancellationToken
await node.StopAsync(CancellationToken.None);

// --- Entité fictive pour le test ---
public class Product : MorpheoEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}