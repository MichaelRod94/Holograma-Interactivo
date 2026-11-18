using Holograma3D.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Holograma3D.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        // Cada fase agrega su IEntityTypeConfiguration en Persistence/Configurations sin tocar este archivo.
        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
