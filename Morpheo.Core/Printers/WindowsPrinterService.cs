using System.Drawing.Printing;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;

namespace Morpheo.Core.Printers;

public record LocalPrinter(string Name, string Group);

public class WindowsPrinterService
{
    private readonly MorpheoOptions _options;
    private readonly ILogger<WindowsPrinterService> _logger;

    public WindowsPrinterService(MorpheoOptions options, ILogger<WindowsPrinterService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public List<LocalPrinter> GetAvailablePrinters()
    {
        var results = new List<LocalPrinter>();

        // 1. Récupérer toutes les imprimantes installées sur Windows
        // (Attention : Sur Linux/Docker, cette liste sera vide ou nécessitera une autre lib comme CUPS)
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("La détection d'imprimantes n'est supportée que sur Windows pour l'instant.");
            return results;
        }

        var installedPrinters = PrinterSettings.InstalledPrinters.Cast<string>().ToList();

        foreach (var printerName in installedPrinters)
        {
            // 2. Vérifier les exclusions (Bloquer les imprimantes Hybrides ou Virtuelles)
            if (IsExcluded(printerName))
            {
                _logger.LogDebug($"🚫 Imprimante ignorée (Exclue) : {printerName}");
                continue;
            }

            // 3. Déterminer le groupe
            string group = DetermineGroup(printerName);

            _logger.LogInformation($"🖨️ Imprimante détectée : {printerName} [{group}]");
            results.Add(new LocalPrinter(printerName, group));
        }

        return results;
    }

    private bool IsExcluded(string name)
    {
        // On vérifie si le nom matche l'une des regex d'exclusion
        return _options.Printers.Exclusions.Any(pattern =>
            Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase));
    }

    private string DetermineGroup(string name)
    {
        // On regarde si le nom matche un groupe défini
        foreach (var group in _options.Printers.Groups)
        {
            foreach (var pattern in group.Value)
            {
                if (Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase))
                    return group.Key; // ex: "KITCHEN"
            }
        }
        return "DEFAULT"; // Groupe par défaut
    }
}