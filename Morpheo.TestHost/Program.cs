using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;
using Morpheo.Core;
using Morpheo.TestHost;

// Titre de la fenêtre console
Console.Title = "Morpheo Test Node";

// 1. Création de l'hôte
var builder = Host.CreateApplicationBuilder(args);

// Configuration des logs
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 2. Injection de Morpheo avec notre TestDbContext
builder.Services.AddMorpheo<TestDbContext>(options =>
{
    // Nom aléatoire pour identifier facilement les fenêtres
    options.NodeName = "CAISSE_" + Random.Shared.Next(100, 999);
    options.Role = NodeRole.StandardClient;
    options.DiscoveryPort = 5555;

    // --- Configuration des Imprimantes ---
    options.Printers
        // 1. On exclut les imprimantes virtuelles ou indésirables
        .Exclude("Microsoft.*")       // PDF, XPS...
        .Exclude("OneNote.*")         // OneNote
        .Exclude("Fax")
        .Exclude(".*Ordonnance.*")    // <--- Cas spécifique demandé (Bloquer les imprimantes mixtes)

        // 2. On regroupe les imprimantes valides par fonction
        .DefineGroup("KITCHEN", ".*Zebra.*")   // Tout ce qui contient "Zebra" va en Cuisine
        .DefineGroup("RECEIPT", ".*Epson.*");  // Tout ce qui contient "Epson" est un ticket
});

var host = builder.Build();

// 3. Récupération et démarrage du Nœud
var node = host.Services.GetRequiredService<MorpheoNode>();

Console.WriteLine("--- DÉMARRAGE DU TEST ---");
await node.StartAsync();

// --- LOGIQUE INTERACTIVE POUR LE TEST ---

// Liste locale pour se souvenir des voisins trouvés
var neighbors = new List<PeerInfo>();

// On s'abonne aux événements pour tenir la liste à jour
node.Discovery.PeerFound += (s, peer) =>
{
    if (!neighbors.Any(n => n.Id == peer.Id))
    {
        neighbors.Add(peer);
    }
};

node.Discovery.PeerLost += (s, peer) =>
{
    neighbors.RemoveAll(n => n.Id == peer.Id);
};

Console.WriteLine("-------------------------------------------------");
Console.WriteLine(" [P] Appuyez sur 'P' pour envoyer une IMPRESSION à un voisin");
Console.WriteLine(" [Q] Appuyez sur 'Q' pour QUITTER");
Console.WriteLine("-------------------------------------------------");

// Boucle principale
while (true)
{
    // On attend une touche (sans l'afficher)
    if (Console.KeyAvailable)
    {
        var key = Console.ReadKey(true).Key;

        if (key == ConsoleKey.Q)
        {
            Console.WriteLine("Arrêt demandé...");
            break;
        }

        if (key == ConsoleKey.P)
        {
            if (neighbors.Count == 0)
            {
                Console.WriteLine("⚠️ Aucun voisin détecté pour le moment.");
            }
            else
            {
                // On prend le premier voisin
                var target = neighbors.First();

                Console.WriteLine($"\n📤 Envoi d'une demande d'impression vers {target.Name}...");

                // On pourrait choisir ici une imprimante spécifique grâce aux Tags
                // var printerTarget = neighbors.FirstOrDefault(n => n.Tags.Any(t => t.Contains("KITCHEN")));

                await node.Client.SendPrintJobAsync(target, "Hello from Morpheo! " + DateTime.Now.ToLongTimeString());
            }
        }
    }

    // Petite pause pour ne pas surcharger le CPU
    await Task.Delay(100);
}

await node.StopAsync();