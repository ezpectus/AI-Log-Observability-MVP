using Api.Middleware;
using Application.Interfaces;
using Application.Services;
using Infrastructure.AI;
using Infrastructure.Background;
using Infrastructure.PostgreSql;
using Infrastructure.Realtime;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddSignalR();

// Configure PostgreSQL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configure Redis with error handling
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var options = ConfigurationOptions.Parse(redisConnectionString);
    options.AbortOnConnectFail = false;
    options.ConnectTimeout = 5000;
    options.SyncTimeout = 5000;
    
    try
    {
        var multiplexer = ConnectionMultiplexer.Connect(options);
        return multiplexer;
    }
    catch (Exception ex)
    {
        try
        {
            var logger = sp.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "Failed to connect to Redis at {RedisConnectionString}. Retrying with abortConnect=false...", redisConnectionString);
        }
        catch
        {
            // Logger not available yet, will handle connection with abortConnect=false
        }
        
        var multiplexer = ConnectionMultiplexer.Connect(options);
        return multiplexer;
    }
});

// Configure HttpClient for HuggingFace
builder.Services.AddHttpClient<HuggingFaceClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Register services
builder.Services.AddScoped<LogRepository>();
builder.Services.AddSingleton<ILogIngestionService, LogIngestionService>();
builder.Services.AddScoped<ILogQueryService, LogQueryService>();
builder.Services.AddScoped<IErrorGroupingService, ErrorGroupingService>();
builder.Services.AddSingleton<MetricsAggregator>();
builder.Services.AddScoped<IAiAnalysisService, HuggingFaceClient>(sp =>
{
    var httpClient = sp.GetRequiredService<HttpClient>();
    var configuration = sp.GetRequiredService<IConfiguration>();
    var apiKey = configuration["HuggingFace:ApiKey"] ?? string.Empty;
    var modelUrl = configuration["HuggingFace:ModelUrl"] ?? string.Empty;
    return new HuggingFaceClient(apiKey, modelUrl, httpClient);
});
builder.Services.AddHostedService<LogWorker>();
builder.Services.AddHostedService<MockDataSeederHostedService>();

// Configure Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("LogIngestionPolicy", context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 2,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10,
            })
    );
});

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors("AllowReact");
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.MapHub<LogHub>("/logs-stream");

app.Run();
