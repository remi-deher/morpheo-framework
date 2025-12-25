using Microsoft.AspNetCore.Http;
using System.Net.Http;

namespace Morpheo.Abstractions;

/// <summary>
/// Définit le contrat pour valider les requêtes entrantes (Le Videur).
/// </summary>
public interface IRequestAuthenticator
{
    /// <summary>
    /// Vérifie si la requête HTTP est légitime.
    /// </summary>
    /// <returns>True si autorisé, False si rejeté.</returns>
    Task<bool> IsAuthorizedAsync(HttpContext context);
}