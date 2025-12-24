using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;
using Morpheo.Core;
using Morpheo.TestHost;

Console.Title = "Morpheo Test Node";

// 1. Création de l'hôte (Comme dans une vraie app)
var builder = Host.CreateApplicationBuilder(args);

// Configuration des logs pour bien voir ce qui se passe
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 2. Injection de Morpheo avec notre TestDbContext
builder.Services.AddMorpheo<TestDbContext>(options =>
{
    options.NodeName = "TEST_PC_" + Random.Shared.Next(100, 999);
    options.Role = NodeRole.StandardClient;
    options.DiscoveryPort = 5555; // Port par défaut
});

var host = builder.Build();

// 3. Récupération et démarrage du Nœud
var node = host.Services.GetRequiredService<MorpheoNode>();

Console.WriteLine("--- DÉMARRAGE DU TEST ---");
await node.StartAsync();

Console.WriteLine("Presser ENTREE pour quitter...");
Console.ReadLine();

await node.StopAsync();