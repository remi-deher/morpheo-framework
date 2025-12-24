using System.ComponentModel.DataAnnotations;

namespace Morpheo.Core.Data;

/// <summary>
/// Classe de base pour toutes les entités qui devront être synchronisées
/// entre les nœuds (Android <-> Windows <-> Linux).
/// </summary>
public abstract class MorpheoEntity
{
    // On utilise des GUID (String) plutôt que des int (AutoIncrement)
    // car en distribué, deux nœuds créeraient l'ID "1" en même temps -> Conflit.
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // Indispensable pour la réconciliation (Vecteur de temps simplifié)
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    // Pour le "Soft Delete" : on ne supprime jamais vraiment une ligne en P2P,
    // on la marque comme supprimée pour propager l'info aux autres.
    public bool IsDeleted { get; set; } = false;
}