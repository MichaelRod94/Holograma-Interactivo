using Holograma3D.Core.Domain.Models;
using Holograma3D.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Holograma3D.Infrastructure.Persistence.Configurations;

public sealed class Model3DConfiguration : IEntityTypeConfiguration<Model3D>
{
    public void Configure(EntityTypeBuilder<Model3D> b)
    {
        b.ToTable("Models");
        b.HasKey(m => m.Id);
        b.Property(m => m.OwnerId).IsRequired().HasMaxLength(450);
        b.HasOne<AppUser>().WithMany().HasForeignKey(m => m.OwnerId).OnDelete(DeleteBehavior.Cascade);
        b.Property(m => m.Name).IsRequired().HasMaxLength(80);
        b.Property(m => m.Format).HasConversion<string>().HasMaxLength(20);
        b.Property(m => m.Source).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(m => new { m.OwnerId, m.CreatedAt });
    }
}
