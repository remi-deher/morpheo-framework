using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Morpheo.Abstractions;
using Morpheo.Core;
using Morpheo.Core.Data; // Nécessaire pour DatabaseInitializer

namespace Morpheo.App;

public partial class App : Application
{
    public static IHost? AppHost { get; private set; }

    public App()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddMorpheo<AppDbContext>(options =>
        {
            options.NodeName = "POS_" + Random.Shared.Next(1, 99);
            options.Role = NodeRole.StandardClient;
            options.DiscoveryPort = 5555;
            options.Printers.DefineGroup("KITCHEN", ".*Zebra.*");
        });

        builder.Services.AddSingleton<MainWindow>();

        AppHost = builder.Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // --- CORRECTION CRITIQUE ---
        // 1. On initialise la BDD AVANT de démarrer les services d'arrière-plan.
        // Cela garantit que la table 'SyncLogs' existe quand le LogCompactionService démarrera.
        using (var scope = AppHost!.Services.CreateScope())
        {
            try
            {
                var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await initializer.InitializeAsync(dbContext);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur fatale BDD : {ex.Message}");
                Shutdown();
                return;
            }
        }

        // 2. Maintenant que la BDD est prête, on peut démarrer l'hôte
        // (Cela lance LogCompactionService qui trouvera bien la table)
        await AppHost.StartAsync();

        // 3. Enfin, on démarre le nœud Morpheo (Web Server, Discovery...)
        var node = AppHost.Services.GetRequiredService<MorpheoNode>();
        // Le CancellationToken est important pour arrêter proprement
        await node.StartAsync(CancellationToken.None);

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