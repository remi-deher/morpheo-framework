using Microsoft.EntityFrameworkCore;
using Morpheo.Core.Data;

namespace Morpheo.TestHost;

// Une BDD vide juste pour tester que le fichier se crée
public class TestDbContext : MorpheoDbContext
{
    public TestDbContext(DbContextOptions options) : base(options) { }
}