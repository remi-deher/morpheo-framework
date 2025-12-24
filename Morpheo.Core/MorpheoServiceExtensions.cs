using Microsoft.Extensions.DependencyInjection;
using Morpheo.Abstractions;

namespace Morpheo.Core;

public static class MorpheoServiceExtensions
{
    public static IServiceCollection AddMorpheo(this IServiceCollection services, Action<MorpheoOptions> configure)
    {
        // 1. Configuration des options
        var options = new MorpheoOptions();
        configure(options);
        services.AddSingleton(options);

        // 2. Enregistrement du service principal (Le Node)
        services.AddSingleton<MorpheoNode>();

        // Note : On n'enregistre PAS INetworkDiscovery ou IPrintGateway ici.
        // C'est l'application hôte (Windows/ Linux / Android) qui devra fournir ces implémentations spécifiques.

        return services;
    }
}