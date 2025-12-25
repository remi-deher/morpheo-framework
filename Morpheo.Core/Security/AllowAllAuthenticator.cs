using Microsoft.AspNetCore.Http;
using Morpheo.Abstractions;

namespace Morpheo.Core.Security;

/// <summary>
/// Stratégie par défaut (Convention) : On laisse tout passer.
/// Idéal pour le développement ou les réseaux isolés sans config.
/// </summary>
public class AllowAllAuthenticator : IRequestAuthenticator
{
    public Task<bool> IsAuthorizedAsync(HttpContext context)
    {
        return Task.FromResult(true);
    }
}