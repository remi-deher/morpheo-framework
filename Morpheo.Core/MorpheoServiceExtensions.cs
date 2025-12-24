using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Morpheo.Abstractions;
using Morpheo.Core.Data;      // Nécessaire pour DatabaseInitializer et MorpheoDbContext
using Morpheo.Core.Discovery; // Nécessaire pour UdpDiscoveryService

namespace Morpheo.Core;

public static class MorpheoServiceExtensions
{
    // On transforme la méthode en "Générique" <TDbContext> pour que l'utilisateur 
    // puisse injecter sa propre classe héritant de MorpheoDbContext.
    public static IServiceCollection AddMorpheo<TDbContext>(
        this IServiceCollection services,
        Action<MorpheoOptions> configure)
        where TDbContext : MorpheoDbContext
    {
        // --- 1. Configuration et Validation des options ---
        var options = new MorpheoOptions();
        configure(options);

        // Sécurité : On vérifie que le port est valide avant d'aller plus loin
        options.Validate();

        services.AddSingleton(options);

        // --- 2. Enregistrement des Services Core ---
        services.AddSingleton<MorpheoNode>();

        // Enregistrement du service de découverte par défaut (UDP Broadcast)
        // L'utilisateur pourra le remplacer s'il le souhaite, mais c'est le standard.
        services.AddSingleton<INetworkDiscovery, UdpDiscoveryService>();

        // --- 3. Configuration de la Base de Données (SQLite) ---

        // Service qui calcule le chemin du fichier .db selon l'OS (Android/Linux/Windows)
        services.AddSingleton<DatabaseInitializer>();

        // Configuration du contexte Entity Framework
        services.AddDbContext<TDbContext>((provider, dbOptions) =>
        {
            // On récupère l'initialiseur pour savoir où poser le fichier
            var initializer = provider.GetRequiredService<DatabaseInitializer>();
            var dbPath = initializer.GetDatabasePath();

            // On connecte SQLite
            dbOptions.UseSqlite($"Data Source={dbPath}");
        });

        // Note : IPrintGateway n'est pas présent pas là, car il reste spécifique à l'OS.

        return services;
    }
    turn services;
    }
}