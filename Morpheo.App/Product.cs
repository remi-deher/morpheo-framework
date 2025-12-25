using Morpheo.Core.Data;

namespace Morpheo.App;

// Hérite de MorpheoEntity pour avoir un ID unique automatique
public class Product : MorpheoEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }

    // Pour l'affichage facile dans la liste WPF
    public string DisplayInfo => $"{Name} - {Price:C}";
}