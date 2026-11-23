using Holograma3D.Core.Domain.Generation;
using Holograma3D.Core.Domain.Models;
using Holograma3D.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Holograma3D.Infrastructure.Persistence.Configurations;

public sealed class GenerationJobConfiguration : IEntityTypeConfiguration<GenerationJob>
{
    public void Configure(EntityTypeBuilder<GenerationJob> b)
    {
        b.ToTable("GenerationJobs");
        b.HasKey(j => j.Id);
        b.Ignore(j => j.IsActive);
        b.Property(j => j.OwnerId).IsRequired().HasMaxLength(450);
        b.HasOne<AppUser>().WithMany().HasForeignKey(j => j.OwnerId).OnDelete(DeleteBehavior.Cascade);
        // Si el usuario borra el modelo generado, la generación se queda sin modelo (no se borra).
        b.HasOne<Model3D>().WithMany().HasForeignKey(j => j.ModelId).OnDelete(DeleteBehavior.SetNull);
        b.Property(j => j.Name).IsRequired().HasMaxLength(80);
        b.Property(j => j.Provider).IsRequired().HasMaxLength(20);
        b.Property(j => j.ProviderTaskId).IsRequired().HasMaxLength(200);
        b.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(j => j.Error).HasMaxLength(500);
        b.HasIndex(j => new { j.OwnerId, j.CreatedAt });
        b.HasIndex(j => j.Status);
    }
}
