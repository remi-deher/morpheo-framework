using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Morpheo.Abstractions;
using Morpheo.Core.Data;
using Morpheo.Core.Discovery;

namespace Morpheo.Core;

public static class MorpheoServiceExtensions
{
    public static IServiceCollection AddMorpheo<TDbContext>(
        this IServiceCollection services,
        Action<MorpheoOptions> configure)
        where TDbContext : MorpheoDbContext
    {
        var options = new MorpheoOptions();
        configure(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton<MorpheoNode>();
        services.AddSingleton<INetworkDiscovery, UdpDiscoveryService>();

        services.AddSingleton<DatabaseInitializer>();

        // On enregistre le contexte spécifique de l'utilisateur (ex: TestDbContext)
        services.AddDbContext<TDbContext>((provider, dbOptions) =>
        {
            var initializer = provider.GetRequiredService<DatabaseInitializer>();
            var dbPath = initializer.GetDatabasePath();
            dbOptions.UseSqlite($"Data Source={dbPath}");
        });

        // On fait le lien (Alias) pour que MorpheoNode puisse le trouver
        // "Si on demande le parent (MorpheoDbContext), renvoie l'enfant (TDbContext)"
        services.AddScoped<MorpheoDbContext>(provider => provider.GetRequiredService<TDbContext>());

        return services;
    }
}