using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;

namespace Morpheo.Core.Sync;

public class ConflictResolutionEngine
{
    private readonly IEntityTypeResolver _typeResolver;
    private readonly ILogger<ConflictResolutionEngine> _logger;

    public ConflictResolutionEngine(IEntityTypeResolver typeResolver, ILogger<ConflictResolutionEngine> logger)
    {
        _typeResolver = typeResolver;
        _logger = logger;
    }

    public string Resolve(string entityName, string localJson, long localTs, string remoteJson, long remoteTs)
    {
        var type = _typeResolver.ResolveType(entityName);

        if (type != null)
        {
            var mergeableInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMergeable<>));

            if (mergeableInterface != null)
            {
                try
                {
                    var localObj = JsonSerializer.Deserialize(localJson, type);
                    var remoteObj = JsonSerializer.Deserialize(remoteJson, type);

                    if (localObj != null && remoteObj != null)
                    {
                        var mergeMethod = mergeableInterface.GetMethod("Merge");
                        var resultObj = mergeMethod!.Invoke(localObj, new[] { remoteObj });
                        return JsonSerializer.Serialize(resultObj, type);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Erreur fusion CRDT {entityName}");
                }
            }
        }

        // Fallback LWW
        return remoteTs > localTs ? remoteJson : localJson;
    }
}