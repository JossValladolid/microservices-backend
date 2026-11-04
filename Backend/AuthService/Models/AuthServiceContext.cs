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

    /// <summary>
    /// Representa la tabla de sesiones en la base de datos.
    /// </summary>
    public DbSet<Session> Sessions {get; set; } = null!;

    /// <summary>
    /// Representa la tabla de metadatos de archivos en la base de datos.
    /// </summary>
    public DbSet<StoredFile> Files {get; set; } = null!;

    /// <summary>
    /// Representa la tabla de carpetas en la base de datos.
    /// </summary>
    public DbSet<Folder> Folders {get; set; } = null!;

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.UserName)
            .IsUnique();

        modelBuilder.Entity<Session>()
            .HasOne(s => s.User)
            .WithMany(u => u.Sessions)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Folder>(e =>
        {
            e.HasOne(f => f.Owner).WithMany().HasForeignKey(f => f.OwnerId).OnDelete(DeleteBehavior.Cascade);
            // NoAction (y no Restrict) para que borrar un usuario pueda eliminar en cascada carpetas y archivos en una sola sentencia.
            e.HasOne(f => f.Parent).WithMany(f => f.Children).HasForeignKey(f => f.ParentId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(f => new { f.OwnerId, f.ParentId, f.Name }).IsUnique().AreNullsDistinct(false);
            // Cada usuario tiene una sola carpeta de nivel raíz: su home.
            e.HasIndex(f => f.OwnerId).IsUnique().HasFilter("\"ParentId\" IS NULL").HasDatabaseName("IX_Folders_OwnerId_Home");
            // El contenido de un usuario desactivado queda oculto (sin duplicar el estado en cada fila).
            e.HasQueryFilter(f => f.Owner!.IsActive);
        });

        modelBuilder.Entity<StoredFile>(e =>
        {
            e.ToTable("Files");
            e.HasOne(f => f.Owner).WithMany().HasForeignKey(f => f.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(f => f.Folder).WithMany(f => f.Files).HasForeignKey(f => f.FolderId).OnDelete(DeleteBehavior.NoAction);
            e.Property(f => f.Status).HasConversion<string>();
            e.HasIndex(f => new { f.OwnerId, f.FolderId, f.Name }).IsUnique().AreNullsDistinct(false);
            e.HasQueryFilter(f => f.Owner!.IsActive);
        });
    }
}
