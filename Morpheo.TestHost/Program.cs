using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;
using Morpheo.Core;
using Morpheo.Core.Data; // Nécessaire pour MorpheoEntity
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

    // Config imprimantes (Pour rappel)
    options.Printers
        .Exclude("Microsoft.*")
        .Exclude("Fax")
        .DefineGroup("KITCHEN", ".*Zebra.*");
});

var host = builder.Build();
var node = host.Services.GetRequiredService<MorpheoNode>();

Console.WriteLine("--- DÉMARRAGE DU TEST ---");
await node.StartAsync();

// Gestion des voisins pour l'affichage
var neighbors = new List<PeerInfo>();
node.Discovery.PeerFound += (s, peer) => { if (!neighbors.Any(n => n.Id == peer.Id)) neighbors.Add(peer); };
node.Discovery.PeerLost += (s, peer) => { neighbors.RemoveAll(n => n.Id == peer.Id); };

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
                await node.Client.SendPrintJobAsync(target, "Ticket #1234 : 1x Café");
            }
        }

        // --- TEST SYNCHRO (NOUVEAU) ---
        if (key == ConsoleKey.S)
        {
            // 1. On simule la création d'un produit
            var newProduct = new Product
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Produit Test " + Random.Shared.Next(1, 100),
                Price = 10.50m
            };

            Console.WriteLine($"\n🔄 Création locale de : {newProduct.Name}");

            // 2. On demande à Morpheo de propager l'info
            // "J'ai créé (CREATE) ce produit, dis-le à tout le monde !"
            await node.Sync.BroadcastChangeAsync(newProduct, "CREATE");
        }
    }
    await Task.Delay(100);
}

await node.StopAsync();

// --- Entité fictive pour le test ---
public class Product : MorpheoEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}