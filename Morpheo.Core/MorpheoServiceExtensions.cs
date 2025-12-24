using Microsoft.Extensions.DependencyInjection;
using Morpheo.Abstractions;

namespace Morpheo.Core;

public static class MorpheoServiceExtensions
{
    public static IServiceCollection AddMorpheo(this IServiceCollection services, Action<MorpheoOptions> configure)
    {
        var options = new MorpheoOptions();
        configure(options);
        services.AddSingleton(options);

        services.AddSingleton<MorpheoNode>();

        // ENREGISTREMENT DU SERVICE DE DÉCOUVERTE PAR DÉFAUT
        // On l'enregistre comme implémentation de INetworkDiscovery
        services.AddSingleton<INetworkDiscovery, Discovery.UdpDiscoveryService>();

        return services;
    }
    turn services;
    }
}