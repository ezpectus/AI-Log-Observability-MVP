using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq; 
using StackExchange.Redis;
using Application.Interfaces;
using Application.Services;
using Infrastructure.AI;
using Infrastructure.Background;
using Infrastructure.PostgreSql;
using Infrastructure.Realtime;
using Api.Middleware;
using System.Net.Http;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add framework services
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddHttpClient<HuggingFaceClient>(c => c.Timeout = TimeSpan.FromSeconds(30));

// Configure dual-mode database (Postgres with fallback to InMemory)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var configuration = builder.Configuration;
    var env = builder.Environment;
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    var isDevelopment = env.IsDevelopment();

    if (!string.IsNullOrWhiteSpace(connectionString) && !isDevelopment)
    {
        try
        {
            // Quick connectivity check to determine availability of PostgreSQL
            using var testConn = new Npgsql.NpgsqlConnection(connectionString);
            testConn.Open();
            options.UseNpgsql(connectionString);
        }
        catch
        {
            options.UseInMemoryDatabase("LogDb");
        }
    }
    else
    {
        options.UseInMemoryDatabase("LogDb");
    }
});

// Configure dual-mode Redis (real Redis or Moq-powered in-memory bypass)
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";

try
{
    // AbortOnConnectFail = true заставит его сразу выкинуть Exception, если Докер выключен
    var opts = ConfigurationOptions.Parse(redisConnectionString);
    opts.AbortOnConnectFail = true; 
    opts.ConnectTimeout = 2000;
    
    var mux = ConnectionMultiplexer.Connect(opts);
    
    builder.Services.AddSingleton<IConnectionMultiplexer>(mux);
    builder.Services.AddSingleton<IDatabase>(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
}
catch
{
    Console.WriteLine("[WARN] Redis is offline. Running in Bypassed/In-Memory Mode (powered by Moq).");

    // Создаем реальную очередь в оперативке для наших логов
    var inMemoryQueue = new ConcurrentQueue<RedisValue>();

    // Генерируем 100% рабочую заглушку для IDatabase
    var mockDb = new Mock<IDatabase>();

    mockDb.Setup(db => db.ListLeftPushAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync((RedisKey key, RedisValue value, When when, CommandFlags flags) =>
          {
              inMemoryQueue.Enqueue(value);
              return inMemoryQueue.Count;
          });

    mockDb.Setup(db => db.ListRightPopAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync((RedisKey key, CommandFlags flags) =>
          {
              return inMemoryQueue.TryDequeue(out var val) ? val : RedisValue.Null;
          });

    // Генерируем заглушку для IConnectionMultiplexer
    var mockMux = new Mock<IConnectionMultiplexer>();
    mockMux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);

    // Регистрируем наши идеальные фейки
    builder.Services.AddSingleton<IConnectionMultiplexer>(mockMux.Object);
    builder.Services.AddSingleton<IDatabase>(mockDb.Object);
}

// Application services
builder.Services.AddScoped<LogRepository>();
builder.Services.AddSingleton<ILogIngestionService, LogIngestionService>();
builder.Services.AddScoped<ILogQueryService, LogQueryService>();
builder.Services.AddScoped<IErrorGroupingService, ErrorGroupingService>();
builder.Services.AddSingleton<MetricsAggregator>();
builder.Services.AddScoped<ICacheService, CacheService>();
builder.Services.AddScoped<IAiAnalysisService, HuggingFaceClient>(sp =>
{
    var http = sp.GetRequiredService<HttpClient>();
    var cfg = sp.GetRequiredService<IConfiguration>();
    var key = cfg["HuggingFace:ApiKey"] ?? string.Empty;
    var url = cfg["HuggingFace:ModelUrl"] ?? string.Empty;
    return new HuggingFaceClient(key, url, http);
});

builder.Services.AddHostedService<LogWorker>();
builder.Services.AddHostedService<MockDataSeederHostedService>();

// Rate limiting
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
            }));
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact", policy =>
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

var app = builder.Build();

// Middleware pipeline
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