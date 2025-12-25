using Morpheo.Core.Data;

namespace Morpheo.App;

// Hérite de MorpheoEntity pour avoir un ID unique automatique
public class Product : MorpheoEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }

    // Propriété d'affichage pour l'UI (non stockée en base métier)
    // On l'ignore pour EF Core dans le AppDbContext si besoin, ou on la laisse en [NotMapped]
    public string VectorClockDisplay { get; set; } = "{}";

    // Pour l'affichage facile dans la liste WPF
    public string DisplayInfo => $"{Name} - {Price:C}";
}