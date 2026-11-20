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
    /// Consulta paginada del contenido de una carpeta (primero subcarpetas y luego archivos). Sin parentId lista los homes.
    /// Filtros: nombre, tipo (MIME o extensión), propietario (nombre o nombre de usuario) como sub-cadenas sin distinguir
    /// mayúsculas, y rango de fechas de subida/creación. Si se filtra por tipo, solo se devuelven archivos.
    /// </summary>
    [HttpGet("contents")]
    public async Task<ActionResult<PagedResult<ContentItem>>> GetContents([FromQuery] ContentsQuery query)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);

        var folders = _context.Folders.Where(f => f.ParentId == query.ParentId);
        var files = _context.Files.Where(f => f.FolderId == query.ParentId && f.Status == FileStatus.Uploaded);

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            var pattern = Paging.ContainsPattern(query.Name);
            folders = folders.Where(f => EF.Functions.ILike(f.Name, pattern));
            files = files.Where(f => EF.Functions.ILike(f.Name, pattern));
        }

        if (!string.IsNullOrWhiteSpace(query.Owner))
        {
            var pattern = Paging.ContainsPattern(query.Owner);
            folders = folders.Where(f => EF.Functions.ILike(f.Owner!.UserName, pattern) || EF.Functions.ILike(f.Owner!.Name, pattern));
            files = files.Where(f => EF.Functions.ILike(f.Owner!.UserName, pattern) || EF.Functions.ILike(f.Owner!.Name, pattern));
        }

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            var type = Paging.Escape(query.Type.Trim().TrimStart('.'));
            var mimePattern = $"%{type}%";
            var extensionPattern = $"%.{type}%";
            folders = folders.Where(f => false);
            files = files.Where(f => EF.Functions.ILike(f.ContentType, mimePattern) || EF.Functions.ILike(f.Name, extensionPattern));
        }

        if (query.DateFrom is { } dateFrom)
        {
            var from = dateFrom.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            folders = folders.Where(f => f.CreatedAt >= from);
            files = files.Where(f => f.CreatedAt >= from);
        }

        if (query.DateTo is { } dateTo)
        {
            var toExclusive = dateTo.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            folders = folders.Where(f => f.CreatedAt < toExclusive);
            files = files.Where(f => f.CreatedAt < toExclusive);
        }

        var folderCount = await folders.CountAsync();
        var fileCount = await files.CountAsync();
        var skip = (page - 1) * pageSize;

        // Las carpetas ocupan las primeras posiciones del listado; los archivos continúan después.
        var items = new List<ContentItem>();
        if (skip < folderCount)
        {
            items.AddRange(await folders
                .OrderBy(f => f.Name)
                .Skip(skip)
                .Take(pageSize)
                .Select(f => new ContentItem
                {
                    Kind = "folder",
                    Id = f.Id,
                    Name = f.Name,
                    OwnerId = f.OwnerId,
                    OwnerName = f.Owner!.UserName,
                    ParentId = f.ParentId,
                    CreatedAt = f.CreatedAt,
                    UpdatedAt = f.UpdatedAt
                })
                .ToListAsync());
        }

        var remaining = pageSize - items.Count;
        if (remaining > 0)
        {
            items.AddRange(await files
                .OrderBy(f => f.Name)
                .Skip(Math.Max(0, skip - folderCount))
                .Take(remaining)
                .Select(f => new ContentItem
                {
                    Kind = "file",
                    Id = f.Id,
                    Name = f.Name,
                    OwnerId = f.OwnerId,
                    OwnerName = f.Owner!.UserName,
                    ParentId = f.FolderId,
                    CreatedAt = f.CreatedAt,
                    UpdatedAt = f.UpdatedAt,
                    Size = f.SizeBytes,
                    ContentType = f.ContentType
                })
                .ToListAsync());
        }

        return new PagedResult<ContentItem>
        {
            Items = items,
            Total = folderCount + fileCount,
            Page = page,
            PageSize = pageSize
        };
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
    /// Crea una subcarpeta dentro de una carpeta propia (un administrador puede crearla en cualquier carpeta).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateFolder([FromBody] CreateFolderRequest request)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (!IsValidFolderName(name))
        {
            return BadRequest(new { message = "El nombre de la carpeta no es válido." });
        }

        var userId = CurrentUserId;
        var isAdmin = IsAdmin;
        var parent = await _context.Folders.FirstOrDefaultAsync(f => f.Id == request.ParentId && (f.OwnerId == userId || isAdmin));
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
    /// Cambia el nombre de una carpeta. Solo el propietario o un admin. La carpeta home no se renombra
    /// (lleva el nombre de usuario; cambia al editar al usuario).
    /// </summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> RenameFolder(Guid id, [FromBody] RenameRequest request)
    {
        var folder = await _context.Folders.FirstOrDefaultAsync(f => f.Id == id);
        if (folder is null) return NotFound();

        if (folder.OwnerId != CurrentUserId && !IsAdmin) return Forbid();

        if (folder.ParentId is null)
        {
            return Conflict(new { message = "La carpeta home no se puede renombrar." });
        }

        var name = (request.Name ?? string.Empty).Trim();
        if (!IsValidFolderName(name))
        {
            return BadRequest(new { message = "El nombre de la carpeta no es válido." });
        }

        if (name == folder.Name)
        {
            return Ok(new { id = folder.Id, name = folder.Name });
        }

        if (await _context.Folders.AnyAsync(f => f.ParentId == folder.ParentId && f.Name == name && f.Id != folder.Id))
        {
            return Conflict(new { message = "Ya existe una carpeta con ese nombre." });
        }

        folder.Name = name;
        folder.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { id = folder.Id, name = folder.Name });
    }

    private static bool IsValidFolderName(string name) => name.Length <= 100 && RenameRequest.IsValidName(name);

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

/// <summary>
/// Filtros y paginación para consultar el contenido de una carpeta.
/// </summary>
public class ContentsQuery
{
    /// <summary>Carpeta a consultar; null para la raíz (homes de los usuarios).</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Página (empieza en 1).</summary>
    public int Page { get; set; } = 1;

    /// <summary>Tamaño de página.</summary>
    public int PageSize { get; set; } = 10;

    /// <summary>Sub-cadena del nombre.</summary>
    public string? Name { get; set; }

    /// <summary>Sub-cadena del tipo MIME o de la extensión (p. ej. "pdf", "image").</summary>
    public string? Type { get; set; }

    /// <summary>Sub-cadena del nombre o nombre de usuario del propietario.</summary>
    public string? Owner { get; set; }

    /// <summary>Fecha de subida mínima (inclusive, UTC).</summary>
    public DateOnly? DateFrom { get; set; }

    /// <summary>Fecha de subida máxima (inclusive, UTC).</summary>
    public DateOnly? DateTo { get; set; }
}

/// <summary>
/// Elemento del listado de una carpeta: subcarpeta o archivo.
/// </summary>
public class ContentItem
{
    /// <summary>"folder" o "file".</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Identificador.</summary>
    public Guid Id { get; set; }

    /// <summary>Nombre.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Identificador del propietario.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Nombre de usuario del propietario.</summary>
    public string OwnerName { get; set; } = string.Empty;

    /// <summary>Carpeta contenedora.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Fecha de creación (subida, en archivos).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Fecha de última modificación.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Tamaño en bytes (solo archivos).</summary>
    public long? Size { get; set; }

    /// <summary>Tipo MIME (solo archivos).</summary>
    public string? ContentType { get; set; }
}
