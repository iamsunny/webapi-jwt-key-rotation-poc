using JwtKeyRotationPoc.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure key store based on environment
var keyStoreType = builder.Configuration["KeyStore:Type"] ?? "InMemory";

switch (keyStoreType.ToLowerInvariant())
{
    case "azure":
        // Azure Key Vault + App Configuration (production, multi-instance)
        builder.Services.AddAzureKeyStore();
        break;
    
    case "distributed":
    case "redis":
        // Redis distributed cache (multi-instance)
        var redisConnection = builder.Configuration.GetConnectionString("Redis");
        if (!string.IsNullOrEmpty(redisConnection))
        {
            builder.Services.AddRedisDistributedKeyStore(redisConnection);
        }
        else
        {
            // Fallback to in-memory distributed cache (for testing only)
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddDistributedKeyStore();
        }
        break;
    
    case "inmemory":
    default:
        // In-memory store (single-instance, development/testing)
        builder.Services.AddInMemoryKeyStore();
        break;
}

builder.Services.AddScoped<IJwtService, JwtService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
