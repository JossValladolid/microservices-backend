using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Controllers;

/// <summary>
/// Controlador para la gestión y transferencia de archivos en S3/MinIO.
/// </summary>
[ApiController]
[Route("api/files")]
public class FilesController : ControllerBase
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;

    /// <summary>
    /// Constructor del controlador de archivos.
    /// </summary>
    public FilesController(IAmazonS3 s3Client, IConfiguration configuration)
    {
        _s3Client = s3Client;
        _bucketName = Environment.GetEnvironmentVariable("MINIO_DEFAULT_BUCKET")
                      ?? configuration["AWS:BucketName"]
                      ?? "images";
    }

    /// <summary>
    /// Lista los archivos almacenados en S3/MinIO con un prefijo opcional.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListFiles([FromQuery] string? prefix = null)
    {
        try
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = string.IsNullOrWhiteSpace(prefix) ? null : prefix.Trim('/')
            };

            var response = await _s3Client.ListObjectsV2Async(request);

            var files = (response.S3Objects ?? new List<S3Object>())
                .Where(o => !o.Key.EndsWith("/"))
                .OrderByDescending(o => o.LastModified)
                .Select(obj =>
                {
                    var rawName = Path.GetFileName(obj.Key);
                    var cleanName = rawName;
                    var underscoreIndex = rawName.IndexOf('_');
                    if (underscoreIndex > 0 && Guid.TryParse(rawName.Substring(0, underscoreIndex), out _))
                    {
                        cleanName = rawName.Substring(underscoreIndex + 1);
                    }

                    return new
                    {
                        key = obj.Key,
                        fileName = cleanName,
                        size = obj.Size,
                        lastModified = obj.LastModified
                    };
                })
                .ToList();

            return Ok(files);
        }
        catch (AmazonS3Exception ex)
        {
            return StatusCode(500, new { message = $"Error al listar archivos: {ex.Message}" });
        }
    }

    /// <summary>
    /// Genera una URL prefirmada para subir un archivo a S3/MinIO por medio de HTTP PUT
    /// </summary>
    [HttpGet("upload-url")]
    public async Task<IActionResult> GetUploadUrl(
        [FromQuery] string fileName, 
        [FromQuery] string? contentType,
        [FromQuery] string? prefix = "uploads")
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return BadRequest(new { message = "fileName query parameter is required." });
        }

        var safeFileName = Path.GetFileName(fileName);
        var cleanPrefix = string.IsNullOrWhiteSpace(prefix) ? "uploads" : prefix.Trim('/', '\\');
        string fileKey = $"{cleanPrefix}/{Guid.NewGuid()}_{safeFileName}";

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = fileKey,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.AddMinutes(15)
        };

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            request.ContentType = contentType;
        }

        string url = await _s3Client.GetPreSignedURLAsync(request);

        return Ok(new 
        { 
            uploadUrl = url, 
            fileKey = fileKey,
            bucket = _bucketName,
            expiresInMinutes = 15
        });
    }

    /// <summary>
    /// Genera una URL prefirmada para ver/descargar un archivo de S3/MinIO por medio de HTTP GET
    /// </summary>
    [HttpGet("download-url")]
    public async Task<IActionResult> GetDownloadUrl(
        [FromQuery] string fileKey,
        [FromQuery] string? downloadName = null)
    {
        if (string.IsNullOrWhiteSpace(fileKey))
        {
            return BadRequest(new { message = "fileKey query parameter is required." });
        }

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = fileKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(30)
        };

        if (!string.IsNullOrWhiteSpace(downloadName))
        {
            request.ResponseHeaderOverrides.ContentDisposition = $"attachment; filename=\"{downloadName}\"";
        }

        string url = await _s3Client.GetPreSignedURLAsync(request);

        return Ok(new 
        { 
            downloadUrl = url, 
            fileKey = fileKey,
            bucket = _bucketName,
            expiresInMinutes = 30
        });
    }

    /// <summary>
    /// Elimina un archivo de S3/MinIO.
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> DeleteFile([FromQuery] string fileKey)
    {
        if (string.IsNullOrWhiteSpace(fileKey))
        {
            return BadRequest(new { message = "fileKey query parameter is required." });
        }

        try
        {
            await _s3Client.DeleteObjectAsync(_bucketName, fileKey);
            return NoContent();
        }
        catch (AmazonS3Exception ex)
        {
            return StatusCode(500, new { message = $"Error al eliminar archivo: {ex.Message}" });
        }
    }
}