using Microsoft.EntityFrameworkCore;

namespace Proyecto.Models;

/// <summary>
/// Representa el contexto de la base de datos para el proyecto.
/// </summary>
public class ProyectoContext : DbContext 
{
    /// <summary>
    /// Constructor del contexto de la base de datos.
    /// </summary>
    /// <param name="options"></param>
    public ProyectoContext(DbContextOptions<ProyectoContext> options)
        : base(options)
    {
        
    }

    /// <summary>
    /// Representa la tabla de usuarios en la base de datos.
    /// </summary>
    public DbSet<User> Users {get; set; } = null!;
}