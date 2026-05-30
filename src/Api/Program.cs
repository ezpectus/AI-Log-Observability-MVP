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

// Configure PostgreSQL with fallback to In-Memory database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    var isDevelopment = builder.Environment.IsDevelopment();
    
    // Try to use PostgreSQL if connection string is configured
    if (!string.IsNullOrEmpty(connectionString) && !isDevelopment)
    {
        try
        {
            options.UseNpgsql(connectionString);
        }
        catch
        {
            // Fallback to In-Memory if connection fails
            options.UseInMemoryDatabase("LogDb");
        }
    }
    else
    {
        // Default to In-Memory for development or when no connection string is provided
        options.UseInMemoryDatabase("LogDb");
    }
});

// Configure Redis with resilient dual-mode strategy (Docker/Online or In-Memory/Bypass)
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    try
    {
        var options = ConfigurationOptions.Parse(redisConnectionString);
        // Explicitly ensure graceful fallback behavior
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 2000;
        options.SyncTimeout = 2000;
        
        var multiplexer = ConnectionMultiplexer.Connect(options);
        
        // Verify connection is actually working
        if (!multiplexer.IsConnected)
        {
            multiplexer.Dispose();
            throw new InvalidOperationException("Redis multiplexer created but not connected.");
        }
        
        return multiplexer;
    }
    catch (Exception ex)
    {
        // Redis is offline or unreachable - gracefully fall back to in-memory/bypassed mode
        Console.WriteLine("[WARN] Redis is offline. Automatically switching to Bypassed/In-Memory Mode.");
        Console.WriteLine($"[WARN] Reason: {ex.GetType().Name} - {ex.Message}");
        
        // Register a NullConnectionMultiplexer placeholder that services can check
        // Services dependent on Redis will continue to function (with in-memory fallback)
        return NullConnectionMultiplexer.Instance;
    }
});

// NullConnectionMultiplexer to allow graceful Redis fallback
file sealed class NullConnectionMultiplexer : IConnectionMultiplexer
{
    public static readonly NullConnectionMultiplexer Instance = new();
    
    public bool IsConnected => false;
    public string ClientName { get; set; }
    public string Configuration => "null";
    public int GetSentinelConnectionCount() => 0;
    public EndPoint[] GetEndPoints(bool configuredOnly = false) => Array.Empty<EndPoint>();
    public IServer GetServer(EndPoint endpoint, object asyncState = null) => NullServer.Instance;
    public IServer GetServer(string host, int port, object asyncState = null) => NullServer.Instance;
    public IServer GetServer(string hostAndPort, object asyncState = null) => NullServer.Instance;
    public IServer GetServer(IPAddress address, int port, object asyncState = null) => NullServer.Instance;
    public IServer[] GetServers() => Array.Empty<IServer>();
    public IDatabase GetDatabase(int db = -1, object asyncState = null) => NullDatabase.Instance;
    public ISubscriber GetSubscriber(object asyncState = null) => NullSubscriber.Instance;
    public ITransaction CreateTransaction(object asyncState = null) => NullTransaction.Instance;
    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Wait(Task task) { }
    public T Wait<T>(Task<T> task) => default;
    public void Wait(Task task, TimeSpan timeout) { }
    public bool Wait(Task task, int timeoutMs) => false;
    public T Wait<T>(Task<T> task, TimeSpan timeout) => default;
    public bool Wait<T>(Task<T> task, int timeoutMs) => false;
    public void Configure(TextWriter writer) { }
    public ValueTask ConfigureAsync(TextWriter writer) => ValueTask.CompletedTask;
    public void Configure(IConfigurationProvider provider = null) { }
    public string GetStatus() => "null";
    public void GetStatus(TextWriter writer) { }
    public void Close(bool allowCommandsToComplete = true) { }
    public ValueTask CloseAsync(bool allowCommandsToComplete = true) => ValueTask.CompletedTask;
    public void ResetStormLog() { }
    public byte[] GetStormLog() => Array.Empty<byte>();
    public void PublishReconfigure(CommandFlags flags = CommandFlags.None) { }
    public ValueTask PublishReconfigureAsync(CommandFlags flags = CommandFlags.None) => ValueTask.CompletedTask;
    public int GetHashSlot(RedisKey key) => 0;
    public ushort GetServerVersion(EndPoint endpoint) => 0;
    public void ExportConfiguration(Stream destination, ExportOptions options = ExportOptions.Default) { }
    public void AddLibraryNameSuffix(string suffix) { }
    public int TimeoutMilliseconds => 0;
    public long OperationCount => 0;
    public bool PreserveAsyncOrder { get; set; }
    public bool IsConnecting => false;
    public bool IncludeDetailInExceptions { get; set; }
    public int StormLogThreshold { get; set; }
    public string ErrorMessage => "Redis is unavailable";
    public ServerSelectionStrategy ServerSelectionStrategy { get; set; }
    
    public event EventHandler<RedisConnectionFailedEventArgs> ConnectionFailed;
    public event EventHandler<ConnectionFailedEventArgs> ConnectionRestored;
    public event EventHandler<EndPointEventArgs> InternalError;
    public event EventHandler<ServerMaintenanceEvent> ServerMaintenanceEvent;
    public event EventHandler<TraceEventArgs> TraceMessage;
    public event EventHandler<EndPointEventArgs> ConfigurationChanged;
    public event EventHandler<EndPointEventArgs> ConfigurationChangedBroadcast;
    public event EventHandler<HashSlotMovedEventArgs> HashSlotMoved;
}

file sealed class NullServer : IServer
{
    public static readonly NullServer Instance = new();
    
    public EndPoint EndPoint => null;
    public ServerType ServerType => ServerType.Standalone;
    public IConnectionMultiplexer Multiplexer => NullConnectionMultiplexer.Instance;
    public bool IsConnected => false;
    
    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Configure(IConfigurationProvider provider = null) { }
    public IGrouping<ServerType, KeyValuePair<EndPoint, ServerInfo>>[] Info(string section = null, CommandFlags flags = CommandFlags.None) => Array.Empty<IGrouping<ServerType, KeyValuePair<EndPoint, ServerInfo>>>();
    public long Execute(string command, params object[] args) => 0;
    public object ExecuteAsync(string command, params object[] args) => null;
    public void Ping(CommandFlags flags = CommandFlags.None) { }
    public ValueTask PingAsync(CommandFlags flags = CommandFlags.None) => ValueTask.CompletedTask;
}

file sealed class NullDatabase : IDatabase
{
    public static readonly NullDatabase Instance = new();
    
    public IConnectionMultiplexer Multiplexer => NullConnectionMultiplexer.Instance;
    public int Database => 0;
    
    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public RedisValue Get(RedisKey key, CommandFlags flags = CommandFlags.None) => RedisValue.Null;
    public ValueTask<RedisValue> GetAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(RedisValue.Null);
    public bool Set(RedisKey key, RedisValue value, TimeSpan? expiry = null, When when = When.Always, CommandFlags flags = CommandFlags.None) => false;
    public ValueTask<bool> SetAsync(RedisKey key, RedisValue value, TimeSpan? expiry = null, When when = When.Always, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(false);
    public bool KeyExists(RedisKey key, CommandFlags flags = CommandFlags.None) => false;
    public ValueTask<bool> KeyExistsAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(false);
    public bool KeyDelete(RedisKey key, CommandFlags flags = CommandFlags.None) => false;
    public ValueTask<bool> KeyDeleteAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(false);
    public long ListLeftPush(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None) => 0;
    public ValueTask<long> ListLeftPushAsync(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(0L);
    public RedisValue ListRightPop(RedisKey key, CommandFlags flags = CommandFlags.None) => RedisValue.Null;
    public ValueTask<RedisValue> ListRightPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(RedisValue.Null);
    public long ListLength(RedisKey key, CommandFlags flags = CommandFlags.None) => 0;
    public ValueTask<long> ListLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(0L);
    public ITransaction CreateTransaction(object asyncState = null) => NullTransaction.Instance;
}

file sealed class NullSubscriber : ISubscriber
{
    public static readonly NullSubscriber Instance = new();
    public IConnectionMultiplexer Multiplexer => NullConnectionMultiplexer.Instance;
    
    public void Subscribe(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags = CommandFlags.None) { }
    public ValueTask SubscribeAsync(RedisChannel channel, Func<RedisChannel, RedisValue, ValueTask> handler, CommandFlags flags = CommandFlags.None) => ValueTask.CompletedTask;
    public long Publish(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None) => 0;
    public ValueTask<long> PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(0L);
    public void Unsubscribe(RedisChannel channel, Action<RedisChannel> handler = null, CommandFlags flags = CommandFlags.None) { }
    public ValueTask UnsubscribeAsync(RedisChannel channel, Action<RedisChannel> handler = null, CommandFlags flags = CommandFlags.None) => ValueTask.CompletedTask;
}

file sealed class NullTransaction : ITransaction
{
    public static readonly NullTransaction Instance = new();
    public IConnectionMultiplexer Multiplexer => NullConnectionMultiplexer.Instance;
    public int Database => 0;
    
    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public bool Execute(CommandFlags flags = CommandFlags.None) => false;
    public ValueTask<bool> ExecuteAsync(CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(false);
}

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
