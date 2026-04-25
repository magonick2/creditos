using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PlataformaCreditos.Models;

namespace PlataformaCreditos.Data;

public class ApplicationDbContext : IdentityDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Tablas de negocio para la plataforma
    public DbSet<Cliente> Clientes { get; set; }
    public DbSet<SolicitudCredito> Solicitudes { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Configuración para la entidad Cliente
        builder.Entity<Cliente>(entity =>
        {
            entity.HasKey(c => c.Id);
            
            // Restricción: IngresosMensuales > 0 (Requerido)
            entity.Property(c => c.IngresosMensuales)
                .IsRequired();

            entity.Property(c => c.UsuarioId)
                .IsRequired();
        });

        // Configuración para la entidad SolicitudCredito
        builder.Entity<SolicitudCredito>(entity =>
        {
            entity.HasKey(s => s.Id);

            // Restricción: MontoSolicitado > 0 (Requerido)
            entity.Property(s => s.MontoSolicitado)
                .IsRequired();

            // Guardar el estado como texto en la BD
            entity.Property(s => s.Estado)
                .HasConversion<string>()
                .IsRequired();

            // Relación con Cliente
            entity.HasOne(s => s.Cliente)
                .WithMany()
                .HasForeignKey(s => s.ClienteId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}