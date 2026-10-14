using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Controllers;

[ApiController]
[Route("api/files")]
public class FilesController : ControllerBase
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;

    public FilesController(IAmazonS3 s3Client, IConfiguration configuration)
    {
        _s3Client = s3Client;
        _bucketName = Environment.GetEnvironmentVariable("MINIO_DEFAULT_BUCKET")
                      ?? configuration["AWS:BucketName"]
                      ?? "images";
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
    /// Genera una URL prefirmada para ver/descargar un archivo a S3/MinIO por medio de HTTP GET
    /// </summary>
    [HttpGet("download-url")]
    public async Task<IActionResult> GetDownloadUrl([FromQuery] string fileKey)
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

        string url = await _s3Client.GetPreSignedURLAsync(request);

        return Ok(new 
        { 
            downloadUrl = url,
            fileKey = fileKey,
            bucket = _bucketName,
            expiresInMinutes = 30
        });
    }
}