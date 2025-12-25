using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Morpheo.Abstractions;
using Morpheo.Core;

namespace Morpheo.App;

public partial class App : Application
{
    // L'hôte qui contient tous nos services (Morpheo + UI)
    public static IHost? AppHost { get; private set; }

    public App()
    {
        var builder = Host.CreateApplicationBuilder();

        // 1. On configure Morpheo
        builder.Services.AddMorpheo<AppDbContext>(options =>
        {
            // Nom aléatoire pour simuler plusieurs caisses
            options.NodeName = "POS_" + Random.Shared.Next(1, 99);
            options.Role = NodeRole.StandardClient;

            // On peut définir un port fixe ou aléatoire. 
            // Pour tester sur le MEME PC, il faut varier les ports.
            // En production, on mettrait 5000 fixe.
            options.DiscoveryPort = 5555;

            options.Printers.DefineGroup("KITCHEN", ".*Zebra.*");
        });

        // 2. On enregistre notre fenêtre principale
        builder.Services.AddSingleton<MainWindow>();

        AppHost = builder.Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await AppHost!.StartAsync();

        // Démarrage du Nœud Morpheo
        var node = AppHost.Services.GetRequiredService<MorpheoNode>();
        await node.StartAsync();

        // Affichage de la fenêtre
        var mainWindow = AppHost.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (AppHost != null)
        {
            await AppHost.StopAsync();
            AppHost.Dispose();
        }
        base.OnExit(e);
    }
}