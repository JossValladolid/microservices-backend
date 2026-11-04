using System.Security.Claims;
using Amazon.S3;
using Amazon.S3.Model;
using AuthService.Filters;
using AuthService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Controllers;

/// <summary>
/// Controlador de carpetas. La jerarquía vive solo en la base de datos (lista de adyacencia con ParentId).
/// La carpeta de nivel raíz de cada usuario es su home; no se crean otras carpetas en la raíz.
/// </summary>
[ApiController]
[Route("api/folders")]
[Authorize]
[ServiceFilter(typeof(ActiveUserFilter))]
public class FoldersController : ControllerBase
{
    private readonly IAmazonS3 _s3Client;
    private readonly AuthServiceContext _context;
    private readonly string _bucketName;

    /// <summary>
    /// Constructor del controlador de carpetas.
    /// </summary>
    public FoldersController(IAmazonS3 s3Client, AuthServiceContext context, IConfiguration configuration)
    {
        _s3Client = s3Client;
        _context = context;
        _bucketName = Environment.GetEnvironmentVariable("MINIO_DEFAULT_BUCKET")
                      ?? configuration["AWS:BucketName"]
                      ?? "images";
    }

    private Guid CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    private bool IsAdmin => User.IsInRole("Admin");

    /// <summary>
    /// Devuelve la carpeta home del usuario autenticado, si faltara la carpeta, la crea.
    /// </summary>
    [HttpGet("home")]
    public async Task<IActionResult> GetHome()
    {
        var userId = CurrentUserId;
        var home = await _context.Folders.FirstOrDefaultAsync(f => f.OwnerId == userId && f.ParentId == null);

        if (home is null)
        {
            var userName = await _context.Users.Where(u => u.Id == userId).Select(u => u.UserName).FirstAsync();
            home = new Folder { OwnerId = userId, Name = userName };
            _context.Folders.Add(home);
            await _context.SaveChangesAsync();
        }

        return Ok(new { id = home.Id, name = home.Name });
    }

    /// <summary>
    /// Lista las subcarpetas de una carpeta; sin parentId lista los homes de los usuarios activos.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListFolders([FromQuery] Guid? parentId = null)
    {
        var folders = await _context.Folders
            .Where(f => f.ParentId == parentId)
            .OrderBy(f => f.Name)
            .Select(f => new { id = f.Id, name = f.Name, ownerId = f.OwnerId, parentId = f.ParentId, createdAt = f.CreatedAt })
            .ToListAsync();

        return Ok(folders);
    }

    /// <summary>
    /// Devuelve una carpeta y su ruta desde el home (para el breadcrumb).
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetFolder(Guid id)
    {
        var folder = await _context.Folders.FirstOrDefaultAsync(f => f.Id == id);
        if (folder is null) return NotFound();

        var path = new List<object>();
        var current = folder;
        while (current is not null)
        {
            path.Insert(0, new { id = current.Id, name = current.Name });
            current = current.ParentId is null ? null : await _context.Folders.FirstOrDefaultAsync(f => f.Id == current.ParentId);
        }

        return Ok(new { id = folder.Id, name = folder.Name, ownerId = folder.OwnerId, parentId = folder.ParentId, path });
    }

    /// <summary>
    /// Crea una subcarpeta dentro de una carpeta propia.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateFolder([FromBody] CreateFolderRequest request)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > 100 || name is "." or ".." || name.Contains('/') || name.Contains('\\'))
        {
            return BadRequest(new { message = "El nombre de la carpeta no es válido." });
        }

        var userId = CurrentUserId;
        var parent = await _context.Folders.FirstOrDefaultAsync(f => f.Id == request.ParentId && f.OwnerId == userId);
        if (parent is null)
        {
            return NotFound(new { message = "Solo puedes crear carpetas dentro de tus propias carpetas." });
        }

        if (await _context.Folders.AnyAsync(f => f.ParentId == parent.Id && f.Name == name))
        {
            return Conflict(new { message = "Ya existe una carpeta con ese nombre." });
        }

        var folder = new Folder { OwnerId = userId, ParentId = parent.Id, Name = name };
        _context.Folders.Add(folder);
        await _context.SaveChangesAsync();

        return Ok(new { id = folder.Id, name = folder.Name, ownerId = folder.OwnerId, parentId = folder.ParentId });
    }

    /// <summary>
    /// Elimina una carpeta. El propietario solo puede borrarla si está vacía; un admin puede borrar carpetas con contenido
    /// (se eliminan recursivamente subcarpetas, archivos y sus objetos en el almacenamiento). La carpeta home no se puede borrar.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteFolder(Guid id)
    {
        var folder = await _context.Folders.FirstOrDefaultAsync(f => f.Id == id);
        if (folder is null) return NotFound();

        if (folder.OwnerId != CurrentUserId && !IsAdmin) return Forbid();

        if (folder.ParentId is null)
        {
            return Conflict(new { message = "La carpeta home no se puede eliminar." });
        }

        var folderIds = new List<Guid> { folder.Id };
        var level = new List<Guid> { folder.Id };
        while (level.Count > 0)
        {
            var parents = level;
            level = await _context.Folders
                .Where(f => parents.Contains(f.ParentId!.Value))
                .Select(f => f.Id)
                .ToListAsync();
            folderIds.AddRange(level);
        }

        var files = await _context.Files.Where(f => folderIds.Contains(f.FolderId!.Value)).ToListAsync();

        if (!IsAdmin && (folderIds.Count > 1 || files.Any(f => f.Status == FileStatus.Uploaded)))
        {
            return Conflict(new { message = "La carpeta debe estar vacía para poder eliminarla." });
        }

        foreach (var chunk in files.Chunk(1000))
        {
            var response = await _s3Client.DeleteObjectsAsync(new DeleteObjectsRequest
            {
                BucketName = _bucketName,
                Objects = chunk.Select(f => new KeyVersion { Key = f.StorageKey }).ToList(),
                Quiet = true
            });

            if (response.DeleteErrors?.Count > 0)
            {
                return StatusCode(500, new { message = "No se pudieron eliminar todos los archivos del almacenamiento." });
            }
        }

        _context.Files.RemoveRange(files);
        _context.Folders.RemoveRange(await _context.Folders.Where(f => folderIds.Contains(f.Id)).ToListAsync());
        await _context.SaveChangesAsync();

        return NoContent();
    }
}

/// <summary>
/// Datos para crear una carpeta.
/// </summary>
public class CreateFolderRequest
{
    /// <summary>Nombre de la carpeta.</summary>
    public string? Name { get; set; }

    /// <summary>Carpeta padre (obligatoria: no se crean carpetas en la raíz).</summary>
    public Guid ParentId { get; set; }
}
