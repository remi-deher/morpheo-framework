using Morpheo.Abstractions;

namespace Morpheo.Core.Data;

public class SimpleTypeResolver : IEntityTypeResolver
{
    private readonly Dictionary<string, Type> _mapping = new();

    public void Register<T>()
    {
        _mapping[typeof(T).Name] = typeof(T);
    }

    public Type? ResolveType(string entityName)
    {
        _mapping.TryGetValue(entityName, out var type);
        return type;
    }
}