using Microsoft.EntityFrameworkCore;

namespace AuthService.Models;

/// <summary>
/// Representa el contexto de la base de datos para el proyecto.
/// </summary>
public class AuthServiceContext : DbContext 
{
    /// <summary>
    /// Constructor del contexto de la base de datos.
    /// </summary>
    /// <param name="options"></param>
    public AuthServiceContext(DbContextOptions<AuthServiceContext> options)
        : base(options)
    {
        
    }

    /// <summary>
    /// Representa la tabla de usuarios en la base de datos.
    /// </summary>
    public DbSet<User> Users {get; set; } = null!;

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();
    }
}