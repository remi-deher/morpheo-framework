namespace Morpheo.Abstractions;


/// Contrat pour toute entité capable de se fusionner elle-même (CRDT).
/// Le Framework utilisera cette méthode en cas de conflit vectoriel.

/// <typeparam name="T">Le type de l'entité (ex: Product, Customer)</typeparam>
public interface IMergeable<T>
{
    
    /// Fusionne l'instance courante (locale) avec une version distante.
    /// Doit retourner une nouvelle instance ou modifier l'existante pour refléter l'état fusionné.
    
    T Merge(T remote);
}


/// Service permettant au moteur de résolution de retrouver le Type C# 
/// à partir du nom de l'entité stocké en BDD ("Product" -> typeof(Morpheo.App.Product)).

public interface IEntityTypeResolver
{
    Type? ResolveType(string entityName);
}