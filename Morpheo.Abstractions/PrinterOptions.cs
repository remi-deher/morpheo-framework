using System.Text.RegularExpressions;

namespace Morpheo.Abstractions;

public class PrinterOptions
{
    // Liste des patterns (Regex) d'imprimantes à ignorer
    public List<string> Exclusions { get; } = new();

    // Dictionnaire : Nom du Groupe -> Liste de patterns
    public Dictionary<string, List<string>> Groups { get; } = new();

    // -- API Fluide pour la configuration --

    /// <summary>
    /// Exclut les imprimantes dont le nom correspond au pattern (ex: "Microsoft.*", "Fax")
    /// </summary>
    public PrinterOptions Exclude(string pattern)
    {
        Exclusions.Add(pattern);
        return this; // Permet de chaîner les appels
    }

    /// <summary>
    /// Crée un groupe (ex: "KITCHEN") et y associe les imprimantes correspondant au pattern
    /// </summary>
    public PrinterOptions DefineGroup(string groupName, string pattern)
    {
        if (!Groups.ContainsKey(groupName))
            Groups[groupName] = new List<string>();

        Groups[groupName].Add(pattern);
        return this;
    }
}