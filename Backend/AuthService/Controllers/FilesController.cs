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
/// Controlador para la gestión y transferencia de archivos en S3/MinIO.
/// Cada archivo tiene una fila de metadatos en la base de datos. Cualquier usuario activo puede ver y descargar cualquier archivo;
/// solo el propietario puede subirlos (a sus propias carpetas) y solo el propietario o un admin pueden borrarlos.
/// </summary>
[ApiController]
[Route("api/files")]
[Authorize]
[ServiceFilter(typeof(ActiveUserFilter))]
public class FilesController : ControllerBase
{
    private const string ChecksumHeader = "x-amz-checksum-sha256";

    private readonly IAmazonS3 _s3Client;
    private readonly IAmazonS3 _presignClient;
    private readonly AuthServiceContext _context;
    private readonly string _bucketName;

    /// <summary>
    /// Constructor del controlador de archivos.
    /// </summary>
    public FilesController(IAmazonS3 s3Client, PresignS3Client presignClient, AuthServiceContext context, IConfiguration configuration)
    {
        _s3Client = s3Client;
        _presignClient = presignClient.Inner;
        _context = context;
        _bucketName = Environment.GetEnvironmentVariable("MINIO_DEFAULT_BUCKET")
                      ?? configuration["AWS:BucketName"]
                      ?? "images";
    }

    private static bool IsSha256Hex(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private Guid CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    private static string HexToBase64(string hex) => Convert.ToBase64String(Convert.FromHexString(hex));

    private bool IsAdmin => User.IsInRole("Admin");

    /// <summary>
    /// Busca un archivo del usuario autenticado. Si no existe o es de otro usuario devuelve null (se responde 404 en ambos casos).
    /// </summary>
    private Task<StoredFile?> FindOwnFile(Guid id) =>
        _context.Files.FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == CurrentUserId);

    /// <summary>
    /// Lista los archivos de una carpeta (de cualquier propietario activo). En la raíz no hay archivos.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListFiles([FromQuery] Guid? folderId)
    {
        var files = await _context.Files
            .Where(f => f.FolderId == folderId && f.Status == FileStatus.Uploaded)
            .OrderByDescending(f => f.UpdatedAt)
            .Select(f => new
            {
                id = f.Id,
                fileName = f.Name,
                contentType = f.ContentType,
                size = f.SizeBytes,
                folderId = f.FolderId,
                ownerId = f.OwnerId,
                lastModified = f.UpdatedAt
            })
            .ToListAsync();

        return Ok(files);
    }

    /// <summary>
    /// Registra el archivo como pendiente y genera una URL prefirmada para subirlo por HTTP PUT.
    /// El SHA-256 declarado por el cliente queda firmado en la URL (header x-amz-checksum-sha256): el almacenamiento rechaza
    /// la subida si el contenido no coincide.
    /// </summary>
    [HttpGet("upload-url")]
    public async Task<IActionResult> GetUploadUrl(
        [FromQuery] string fileName,
        [FromQuery] string sha256,
        [FromQuery] string? contentType,
        [FromQuery] Guid? folderId)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return BadRequest(new { message = "fileName query parameter is required." });
        }

        if (string.IsNullOrWhiteSpace(sha256) || !IsSha256Hex(sha256))
        {
            return BadRequest(new { message = "sha256 debe ser un hash SHA-256 en hexadecimal (64 caracteres)." });
        }

        var userId = CurrentUserId;

        if (folderId is null)
        {
            return BadRequest(new { message = "No se pueden subir archivos a la raíz: indica una carpeta (folderId)." });
        }

        if (!await _context.Folders.AnyAsync(f => f.Id == folderId && f.OwnerId == userId))
        {
            return NotFound(new { message = "Solo puedes subir archivos a tus propias carpetas." });
        }

        var safeFileName = Path.GetFileName(fileName);
        var finalContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;

        var existing = await _context.Files
            .FirstOrDefaultAsync(f => f.OwnerId == userId && f.FolderId == folderId && f.Name == safeFileName);

        if (existing is not null)
        {
            if (existing.Status == FileStatus.Uploaded)
            {
                return Conflict(new { message = "Ya existe un archivo con ese nombre en esta carpeta." });
            }

            // Subida anterior que nunca se confirmó: se descarta para poder reintentar.
            await TryDeleteObject(existing.StorageKey);
            _context.Files.Remove(existing);
        }

        var file = new StoredFile
        {
            OwnerId = userId,
            FolderId = folderId,
            Name = safeFileName,
            ContentType = finalContentType,
            Sha256 = sha256.ToLowerInvariant()
        };
        _context.Files.Add(file);
        await _context.SaveChangesAsync();

        var checksumBase64 = HexToBase64(file.Sha256);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = file.StorageKey,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.AddMinutes(15),
            ContentType = finalContentType
        };
        request.Headers[ChecksumHeader] = checksumBase64;

        string url = await _presignClient.GetPreSignedURLAsync(request);

        return Ok(new
        {
            uploadUrl = url,
            fileId = file.Id,
            uploadHeaders = new Dictionary<string, string>
            {
                ["Content-Type"] = finalContentType,
                [ChecksumHeader] = checksumBase64
            },
            expiresInMinutes = 15
        });
    }

    /// <summary>
    /// Confirma una subida: verifica en el almacenamiento que el objeto exista y que su SHA-256 sea el declarado.
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> ConfirmUpload(Guid id)
    {
        var file = await FindOwnFile(id);
        if (file is null) return NotFound();

        if (file.Status == FileStatus.Uploaded) return Ok(new { id = file.Id });

        GetObjectMetadataResponse metadata;
        try
        {
            metadata = await HeadObject(file.StorageKey);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Conflict(new { message = "El archivo no se encontró en el almacenamiento." });
        }

        if (!ChecksumMatches(metadata, file))
        {
            await TryDeleteObject(file.StorageKey);
            _context.Files.Remove(file);
            await _context.SaveChangesAsync();
            return Conflict(new { message = "La integridad del archivo subido no coincide con el hash declarado." });
        }

        file.SizeBytes = metadata.ContentLength;
        file.Status = FileStatus.Uploaded;
        file.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { id = file.Id });
    }

    /// <summary>
    /// Genera una URL prefirmada para ver/descargar un archivo (de cualquier propietario) por medio de HTTP GET.
    /// Antes de emitirla comprueba que el objeto almacenado siga coincidiendo con el hash registrado;
    /// el cliente debe volver a calcular el SHA-256 de lo descargado y compararlo con <c>sha256</c>.
    /// </summary>
    [HttpGet("{id:guid}/download-url")]
    public async Task<IActionResult> GetDownloadUrl(Guid id)
    {
        var file = await _context.Files.FirstOrDefaultAsync(f => f.Id == id);
        if (file is null || file.Status != FileStatus.Uploaded) return NotFound();

        try
        {
            var metadata = await HeadObject(file.StorageKey);
            if (metadata.ContentLength != file.SizeBytes || !ChecksumMatches(metadata, file))
            {
                return Conflict(new { message = "El archivo almacenado no coincide con el original (posible modificación)." });
            }
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { message = "El archivo no se encontró en el almacenamiento." });
        }

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = file.StorageKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(30)
        };

        string url = await _presignClient.GetPreSignedURLAsync(request);

        return Ok(new
        {
            downloadUrl = url,
            fileName = file.Name,
            contentType = file.ContentType,
            size = file.SizeBytes,
            sha256 = file.Sha256,
            expiresInMinutes = 30
        });
    }

    /// <summary>
    /// Elimina un archivo de S3/MinIO y su registro. Solo el propietario o un admin.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteFile(Guid id)
    {
        var file = await _context.Files.FirstOrDefaultAsync(f => f.Id == id);
        if (file is null) return NotFound();

        if (file.OwnerId != CurrentUserId && !IsAdmin) return Forbid();

        try
        {
            await _s3Client.DeleteObjectAsync(_bucketName, file.StorageKey);
        }
        catch (AmazonS3Exception ex)
        {
            return StatusCode(500, new { message = $"Error al eliminar archivo: {ex.Message}" });
        }

        _context.Files.Remove(file);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private Task<GetObjectMetadataResponse> HeadObject(string key) =>
        _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = _bucketName,
            Key = key,
            ChecksumMode = ChecksumMode.ENABLED
        });

    private static bool ChecksumMatches(GetObjectMetadataResponse metadata, StoredFile file) =>
        string.Equals(metadata.ChecksumSHA256, HexToBase64(file.Sha256), StringComparison.Ordinal);

    private async Task TryDeleteObject(string key)
    {
        try
        {
            await _s3Client.DeleteObjectAsync(_bucketName, key);
        }
        catch (AmazonS3Exception)
        {
        }
    }
}
