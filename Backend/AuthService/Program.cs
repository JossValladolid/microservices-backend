using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using AuthService.Models;
using AuthService.Services;
using Scalar.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Amazon.Extensions.NETCore.Setup;
using Amazon.S3;

try
{
    Env.TraversePath().Load();
}
catch (FileNotFoundException)
{

}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowViteLocal", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
               .AllowAnyMethod()
               .AllowAnyHeader()
               .AllowCredentials();
    });
});

var dbHost = Environment.GetEnvironmentVariable("DB_HOST");
var dbPort = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
var dbName = Environment.GetEnvironmentVariable("DB_NAME");
var dbUser = Environment.GetEnvironmentVariable("DB_USER");
var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD");

var tokenService = new TokenService();
builder.Services.AddSingleton(tokenService);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = tokenService.ValidationParameters;

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.Token = context.Request.Cookies[TokenService.AccessTokenCookie];
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddAuthorization();

var connectionString = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPassword};";

builder.Services.AddDbContext<AuthServiceContext>(opt =>
opt.UseNpgsql(connectionString));

builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        // Docker bridge networks live in 172.16.0.0/12
        options.KnownIPNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.Parse("172.16.0.0"), 12));
    });
        
var minioAccessKey = Environment.GetEnvironmentVariable("MINIO_SVCACCT_ACCESS_KEY")
                     ?? Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID")
                     ?? builder.Configuration["AWS:AccessKey"];

var minioSecretKey = Environment.GetEnvironmentVariable("MINIO_SVCACCT_SECRET_KEY")
                     ?? Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY")
                     ?? builder.Configuration["AWS:SecretKey"];

var minioServiceUrl = Environment.GetEnvironmentVariable("MINIO_S3_URL")
                      ?? builder.Configuration["AWS:ServiceURL"]
                      ?? "http://minio:9000";

var minioRegion = Environment.GetEnvironmentVariable("AWS_REGION")
                  ?? builder.Configuration["AWS:Region"]
                  ?? "us-east-1";

builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var s3Config = new AmazonS3Config
    {
        ServiceURL = minioServiceUrl,
        ForcePathStyle = true,
        AuthenticationRegion = minioRegion,
        HttpClientFactory = new LocalDevHttpClientFactory()
    };

    if (!string.IsNullOrEmpty(minioAccessKey) && !string.IsNullOrEmpty(minioSecretKey))
    {
        return new AmazonS3Client(minioAccessKey, minioSecretKey, s3Config);
    }

    return new AmazonS3Client(s3Config);
});

// Separate client used only for generating presigned URLs — points at the public-facing
// nginx address so the URLs the browser receives use https://localhost, not http://minio:9000.
var minioPublicUrl = Environment.GetEnvironmentVariable("MINIO_PUBLIC_URL") ?? "https://localhost";
builder.Services.AddSingleton<PresignS3Client>(_ =>
{
    var s3Config = new AmazonS3Config
    {
        ServiceURL = minioPublicUrl,
        ForcePathStyle = true,
        AuthenticationRegion = minioRegion,
        HttpClientFactory = new LocalDevHttpClientFactory()
    };

    var client = !string.IsNullOrEmpty(minioAccessKey) && !string.IsNullOrEmpty(minioSecretKey)
        ? new AmazonS3Client(minioAccessKey, minioSecretKey, s3Config)
        : new AmazonS3Client(s3Config);

    return new PresignS3Client(client);
});

var app = builder.Build();

app.UseForwardedHeaders();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuthServiceContext>();
    db.Database.Migrate();

    var seedDemoUsers = Environment.GetEnvironmentVariable("SEED_DEMO_USERS");
    if (string.Equals(seedDemoUsers, "true", StringComparison.OrdinalIgnoreCase))
    {
        var seedFilePath = Path.Combine(AppContext.BaseDirectory, "Seed", "seed_users.sql");
        if (File.Exists(seedFilePath))
        {
            db.Database.ExecuteSqlRaw(File.ReadAllText(seedFilePath));
        }
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors("AllowViteLocal");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public class LocalDevHttpClientFactory : Amazon.Runtime.HttpClientFactory
{
    public override HttpClient CreateHttpClient(Amazon.Runtime.IClientConfig clientConfig)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        };
        return new HttpClient(handler);
    }
}

// Wrapper so DI can distinguish the presigning client from the internal operations client.
public class PresignS3Client(IAmazonS3 inner)
{
    public IAmazonS3 Inner { get; } = inner;
}
