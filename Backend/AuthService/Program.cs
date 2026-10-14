using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using AuthService.Models;
using AuthService.Services;
using Scalar.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

var minioAccessKey = Environment.GetEnvironmentVariable("MINIO_SVCACCT_ACCESS_KEY")
                     ?? Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID")
                     ?? builder.Configuration["AWS:AccessKey"];

var minioSecretKey = Environment.GetEnvironmentVariable("MINIO_SVCACCT_SECRET_KEY")
                     ?? Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY")
                     ?? builder.Configuration["AWS:SecretKey"];

var minioServiceUrl = Environment.GetEnvironmentVariable("MINIO_S3_URL")
                      ?? builder.Configuration["AWS:ServiceURL"]
                      ?? "https://s3.localhost";

var minioRegion = Environment.GetEnvironmentVariable("AWS_REGION")
                  ?? builder.Configuration["AWS:Region"]
                  ?? "us-east-1";

builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var s3Config = new AmazonS3Config
    {
        ServiceURL = minioServiceUrl,
        ForcePathStyle = true,
        AuthenticationRegion = minioRegion
    };

    if (!string.IsNullOrEmpty(minioAccessKey) && !string.IsNullOrEmpty(minioSecretKey))
    {
        return new AmazonS3Client(minioAccessKey, minioSecretKey, s3Config);
    }

    return new AmazonS3Client(s3Config);
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

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
