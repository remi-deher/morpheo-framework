namespace Morpheo.Abstractions;

/// <summary>
/// Représente une modification de donnée qui transite sur le réseau.
/// </summary>
public record SyncLogDto(
    string Id,          // ID unique de l'événement de synchro
    string EntityId,    // ID de l'objet modifié (ex: ID du Produit)
    string EntityName,  // Type d'objet (ex: "Product")
    string JsonData,    // L'objet complet en JSON
    string Action,      // "CREATE", "UPDATE", "DELETE"
    long Timestamp      // L'heure universelle (Ticks) pour gérer les conflits
);