using Microsoft.EntityFrameworkCore;
using Morpheo.Core.Data;

namespace Morpheo.App;

public class AppDbContext : MorpheoDbContext
{
    public AppDbContext(DbContextOptions options) : base(options) { }

    // Vos tables métiers
    public DbSet<Product> Products { get; set; }
}