using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuthService.Models;

/// <summary>
/// Estado de un archivo respecto a su subida a S3/MinIO.
/// </summary>
public enum FileStatus
{
    /// <summary>Se emitió la URL de subida pero aún no se confirmó que el objeto exista.</summary>
    Pending,

    /// <summary>El objeto existe en el almacenamiento y su integridad fue verificada.</summary>
    Uploaded
}

/// <summary>
/// Metadatos de un archivo guardado en S3/MinIO. El objeto vive en la raíz del bucket con el <c>Id</c> como key (sin prefijos).
/// </summary>
public class StoredFile
{
    /// <summary>
    /// Identificador único del archivo (también forma parte de la key en el almacenamiento).
    /// </summary>
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Identificador del usuario propietario.
    /// </summary>
    [Required]
    public Guid OwnerId { get; set; }

    /// <summary>
    /// Propiedad de navegación para el propietario.
    /// </summary>
    [ForeignKey("OwnerId")]
    public User? Owner { get; set; }

    /// <summary>
    /// Carpeta que contiene al archivo; null si está en la raíz.
    /// </summary>
    public Guid? FolderId { get; set; }

    /// <summary>
    /// Propiedad de navegación para la carpeta contenedora.
    /// </summary>
    [ForeignKey("FolderId")]
    public Folder? Folder { get; set; }

    /// <summary>
    /// Nombre actual del archivo.
    /// </summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Nombre anterior del archivo (último renombrado); null si nunca se renombró.
    /// </summary>
    public string? PreviousName { get; set; }

    /// <summary>
    /// Tipo MIME del archivo.
    /// </summary>
    [Required]
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>
    /// Tamaño real en bytes, tomado del almacenamiento al confirmar la subida.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// SHA-256 del contenido en hexadecimal (64 caracteres, minúsculas).
    /// </summary>
    [Required]
    [StringLength(64, MinimumLength = 64)]
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>
    /// Estado de la subida.
    /// </summary>
    public FileStatus Status { get; set; } = FileStatus.Pending;

    /// <summary>
    /// Fecha de creación.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Fecha de actualización.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Key del objeto en el bucket.
    /// </summary>
    [NotMapped]
    public string StorageKey => Id.ToString();
}

/// <summary>
/// Carpeta lógica de un usuario. La jerarquía existe solo en la base de datos (lista de adyacencia con <see cref="ParentId"/>);
/// en el almacenamiento los objetos son planos.
/// </summary>
public class Folder
{
    /// <summary>
    /// Identificador único de la carpeta.
    /// </summary>
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Identificador del usuario que creó la carpeta y es su propietario.
    /// </summary>
    [Required]
    public Guid OwnerId { get; set; }

    /// <summary>
    /// Propiedad de navegación para el propietario.
    /// </summary>
    [ForeignKey("OwnerId")]
    public User? Owner { get; set; }

    /// <summary>
    /// Carpeta padre; null si está en la raíz.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Propiedad de navegación para la carpeta padre.
    /// </summary>
    [ForeignKey("ParentId")]
    public Folder? Parent { get; set; }

    /// <summary>
    /// Nombre de la carpeta.
    /// </summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Fecha de creación.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Fecha de actualización.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Subcarpetas directas.
    /// </summary>
    public ICollection<Folder> Children { get; set; } = new List<Folder>();

    /// <summary>
    /// Archivos directos de la carpeta.
    /// </summary>
    public ICollection<StoredFile> Files { get; set; } = new List<StoredFile>();
}
