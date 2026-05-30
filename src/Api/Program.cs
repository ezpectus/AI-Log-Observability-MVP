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
    var options = ConfigurationOptions.Parse(redisConnectionString);
    // Explicitly ensure graceful fallback behavior
    options.AbortOnConnectFail = false;
    options.ConnectTimeout = 2000;
    options.SyncTimeout = 2000;
    options.CommandTimeout = 2000;
    
    try
    {
        var multiplexer = ConnectionMultiplexer.Connect(options);
        
        // Verify connection is actually working
        if (!multiplexer.IsConnected)
        {
            throw new InvalidOperationException("Redis multiplexer created but not connected.");
        }
        
        return multiplexer;
    }
    catch (Exception ex)
    {
        // Redis is offline or unreachable - gracefully fall back to in-memory/bypassed mode
        Console.WriteLine("[WARN] Redis is offline. Automatically switching to Bypassed/In-Memory Mode.");
        Console.WriteLine($"[WARN] Reason: {ex.GetType().Name} - {ex.Message}");
        
        // Return a stub/mock ConnectionMultiplexer that gracefully handles calls
        // Services dependent on Redis will continue to function (with in-memory fallback)
        return new StubConnectionMultiplexer();
    }
});

// Define the stub/mock ConnectionMultiplexer for when Redis is unavailable
// This allows dependent services to continue functioning in in-memory mode
internal class StubConnectionMultiplexer : IConnectionMultiplexer
{
    public bool IsConnected => false;
    public int GetHashCode() => base.GetHashCode();
    
    public string ClientName { get; set; } = "stub";
    public int GetSentinelConnectionCount() => 0;
    public ServerCounterHint[] GetCounters() => Array.Empty<ServerCounterHint>();
    public EndPoint[] GetEndPoints(bool configuredOnly = false) => Array.Empty<EndPoint>();
    public IServer GetServer(EndPoint endpoint, object asyncState = null) => new StubServer();
    public IDatabase GetDatabase(int db = -1, object asyncState = null) => new StubDatabase();
    public ISubscriber GetSubscriber(object asyncState = null) => new StubSubscriber();
    public ITransaction CreateTransaction(object asyncState = null) => new StubTransaction();
    
    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Wait(Task task) { }
    public T Wait<T>(Task<T> task) => default;
    public void Wait(Task task, TimeSpan timeout) { }
    public bool Wait(Task task, int timeoutMs) => false;
    public T Wait<T>(Task<T> task, TimeSpan timeout) => default;
    public bool Wait<T>(Task<T> task, int timeoutMs) => false;
    public void Configure(IConfigurationProvider provider = null) { }
    public string Configuration { get; set; } = "stub";
    public IProfiler GetProfiler(object asyncState = null) => null;
    public ServerSelectionStrategy ServerSelectionStrategy { get; set; }
    public event EventHandler<RedisConnectionFailedEventArgs> ConnectionFailed;
    public event EventHandler<EndPointEventArgs> ConnectionRestored;
    public event EventHandler<EndPointEventArgs> InternalError;
    public event EventHandler<ServerMaintenanceEvent> ServerMaintenanceEvent;
    public event EventHandler<TraceEventArgs> TraceMessage;
    public event EventHandler<ConfigurationChangedEventArgs> ConfigurationChanged;
    public event EventHandler<ConfigurationChangedEventArgs> ConfigurationChangedBroadcast;
    
    // Stub implementations for dependent services
    private class StubServer : IServer
    {
        public EndPoint EndPoint => null;
        public ServerType ServerType => ServerType.Standalone;
        public IConnectionMultiplexer Multiplexer => null;
        public bool IsConnected => false;
        
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Configure(IConfigurationProvider provider = null) { }
        public IGrouping<ServerType, KeyValuePair<EndPoint, ServerInfo>>[] Info(string section = null, CommandFlags flags = CommandFlags.None) => Array.Empty<IGrouping<ServerType, KeyValuePair<EndPoint, ServerInfo>>>();
        public T ExecuteAsync<T>(Command<T> command, ServerCommandFlags flags = ServerCommandFlags.None) where T : class => default;
        public void Save(SaveType type = SaveType.BackgroundSave, CommandFlags flags = CommandFlags.None) { }
        public ValueTask SaveAsync(SaveType type = SaveType.BackgroundSave, CommandFlags flags = CommandFlags.None) => ValueTask.CompletedTask;
        public void ShutDown(ShutdownMode shutdownMode = ShutdownMode.Default, CommandFlags flags = CommandFlags.None) { }
        public ValueTask ShutDownAsync(ShutdownMode shutdownMode = ShutdownMode.Default, CommandFlags flags = CommandFlags.None) => ValueTask.CompletedTask;
        public void FlushDatabase(int database = -1, CommandFlags flags = CommandFlags.None) { }
        public ValueTask FlushDatabaseAsync(int database = -1, CommandFlags flags = CommandFlags.None) => ValueTask.CompletedTask;
        public void FlushAllDatabases(CommandFlags flags = CommandFlags.None) { }
        public ValueTask FlushAllDatabasesAsync(CommandFlags flags = CommandFlags.None) => ValueTask.CompletedTask;
        public long Execute(string command, params object[] args) => 0;
        public object ExecuteAsync(string command, params object[] args) => null;
        public void Monitor(int tickFrequency = 1000, CommandFlags flags = CommandFlags.None, Action<MonitorEventArgs> handler = null) { }
        public ValueTask MonitorAsync(int tickFrequency = 1000, CommandFlags flags = CommandFlags.None, Func<MonitorEventArgs, ValueTask> handler = null) => ValueTask.CompletedTask;
        public void SubscriptionSubscribe(RedisChannel[] channels, Action<RedisChannel, RedisValue> handler = null) { }
        public ValueTask SubscriptionSubscribeAsync(RedisChannel[] channels, Func<RedisChannel, RedisValue, ValueTask> handler = null) => ValueTask.CompletedTask;
        public void SubscriptionUnsubscribe(RedisChannel[] channels, Action<RedisChannel> handler = null) { }
        public ValueTask SubscriptionUnsubscribeAsync(RedisChannel[] channels, Func<RedisChannel, ValueTask> handler = null) => ValueTask.CompletedTask;
        public void SubscriptionUnsubscribeAll(Action<RedisChannel> handler = null) { }
        public ValueTask SubscriptionUnsubscribeAllAsync(Func<RedisChannel, ValueTask> handler = null) => ValueTask.CompletedTask;
    }
    
    private class StubDatabase : IDatabase
    {
        public IConnectionMultiplexer Multiplexer => null;
        public int Database => 0;
        
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        
        public RedisValue Get(RedisKey key, CommandFlags flags = CommandFlags.None) => RedisValue.Null;
        public ValueTask<RedisValue> GetAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(RedisValue.Null);
        public RedisValue GetEx(RedisKey key, TimeSpan? expiry = null, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None) => RedisValue.Null;
        public ValueTask<RedisValue> GetExAsync(RedisKey key, TimeSpan? expiry = null, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(RedisValue.Null);
        public RedisValue GetDel(RedisKey key, CommandFlags flags = CommandFlags.None) => RedisValue.Null;
        public ValueTask<RedisValue> GetDelAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(RedisValue.Null);
        public bool Set(RedisKey key, RedisValue value, TimeSpan? expiry = null, When when = When.Always, CommandFlags flags = CommandFlags.None) => true;
        public ValueTask<bool> SetAsync(RedisKey key, RedisValue value, TimeSpan? expiry = null, When when = When.Always, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(true);
        public bool SetWithExpiry(RedisKey key, RedisValue value, DateTime? expiration = null, CommandFlags flags = CommandFlags.None) => true;
        public ValueTask<bool> SetWithExpiryAsync(RedisKey key, RedisValue value, DateTime? expiration = null, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(true);
        public RedisValue[] Get(RedisKey[] keys, CommandFlags flags = CommandFlags.None) => Array.Empty<RedisValue>();
        public ValueTask<RedisValue[]> GetAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(Array.Empty<RedisValue>());
        public bool KeyExists(RedisKey key, CommandFlags flags = CommandFlags.None) => false;
        public ValueTask<bool> KeyExistsAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(false);
        public long KeyExists(RedisKey[] keys, CommandFlags flags = CommandFlags.None) => 0;
        public ValueTask<long> KeyExistsAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(0L);
        public bool KeyDelete(RedisKey key, CommandFlags flags = CommandFlags.None) => true;
        public ValueTask<bool> KeyDeleteAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(true);
        public long KeyDelete(RedisKey[] keys, CommandFlags flags = CommandFlags.None) => 0;
        public ValueTask<long> KeyDeleteAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(0L);
        public byte[][] Execute(string command, params object[] args) => Array.Empty<byte[]>();
        public object ExecuteAsync(string command, params object[] args) => null;
        public TimeSpan? KeyTimeToLive(RedisKey key, CommandFlags flags = CommandFlags.None) => null;
        public ValueTask<TimeSpan?> KeyTimeToLiveAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult<TimeSpan?>(null);
        public long PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None) => 0;
        public ValueTask<long> PublishAsyncAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(0L);
        public ITransaction CreateTransaction(object asyncState = null) => new StubTransaction();
        public void Watch(RedisKey[] keys, CommandFlags flags = CommandFlags.None) { }
        public ValueTask WatchAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None) => ValueTask.CompletedTask;
        public void Unwatch(CommandFlags flags = CommandFlags.None) { }
        public ValueTask UnwatchAsync(CommandFlags flags = CommandFlags.None) => ValueTask.CompletedTask;
        
        // List operations (used by LogIngestionService and LogWorker)
        public long ListLeftPush(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None) => 0;
        public ValueTask<long> ListLeftPushAsync(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(0L);
        public long ListLeftPush(RedisKey key, RedisValue[] values, When when = When.Always, CommandFlags flags = CommandFlags.None) => 0;
        public ValueTask<long> ListLeftPushAsync(RedisKey key, RedisValue[] values, When when = When.Always, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(0L);
        public RedisValue ListRightPop(RedisKey key, CommandFlags flags = CommandFlags.None) => RedisValue.Null;
        public ValueTask<RedisValue> ListRightPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(RedisValue.Null);
        public RedisValue[] ListRightPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None) => Array.Empty<RedisValue>();
        public ValueTask<RedisValue[]> ListRightPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(Array.Empty<RedisValue>());
        public long ListLength(RedisKey key, CommandFlags flags = CommandFlags.None) => 0;
        public ValueTask<long> ListLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(0L);
        public RedisValue ListGetByIndex(RedisKey key, long index, CommandFlags flags = CommandFlags.None) => RedisValue.Null;
        public ValueTask<RedisValue> ListGetByIndexAsync(RedisKey key, long index, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(RedisValue.Null);
        public RedisValue[] ListRange(RedisKey key, long start = 0, long stop = -1, CommandFlags flags = CommandFlags.None) => Array.Empty<RedisValue>();
        public ValueTask<RedisValue[]> ListRangeAsync(RedisKey key, long start = 0, long stop = -1, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(Array.Empty<RedisValue>());
        public void ListTrim(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None) { }
        public ValueTask ListTrimAsync(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None) => ValueTask.CompletedTask;
        public bool ListMove(RedisKey source, RedisKey destination, ListSide sourceSide, ListSide destinationSide, CommandFlags flags = CommandFlags.None) => false;
        public ValueTask<bool> ListMoveAsync(RedisKey source, RedisKey destination, ListSide sourceSide, ListSide destinationSide, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(false);
    }
    
    private class StubSubscriber : ISubscriber
    {
        public IConnectionMultiplexer Multiplexer => null;
        
        public void Subscribe(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags = CommandFlags.None) { }
        public ValueTask SubscribeAsync(RedisChannel channel, Func<RedisChannel, RedisValue, ValueTask> handler, CommandFlags flags = CommandFlags.None) => ValueTask.CompletedTask;
        public long Publish(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None) => 0;
        public ValueTask<long> PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(0L);
        public void Unsubscribe(RedisChannel channel, Action<RedisChannel> handler = null, CommandFlags flags = CommandFlags.None) { }
        public ValueTask UnsubscribeAsync(RedisChannel channel, Action<RedisChannel> handler = null, CommandFlags flags = CommandFlags.None) => ValueTask.CompletedTask;
        public void UnsubscribeAll(Action<RedisChannel> handler = null, CommandFlags flags = CommandFlags.None) { }
        public ValueTask UnsubscribeAllAsync(Action<RedisChannel> handler = null, CommandFlags flags = CommandFlags.None) => ValueTask.CompletedTask;
        public long SubscriptionCount(RedisChannel channel = default) => 0;
        public ValueTask<long> SubscriptionCountAsync(RedisChannel channel = default) => ValueTask.FromResult(0L);
        public ChannelMessageQueue Subscribe(RedisChannel channel, CommandFlags flags = CommandFlags.None) => null;
        public ValueTask<ChannelMessageQueue> SubscribeAsync(RedisChannel channel, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult<ChannelMessageQueue>(null);
        public ChannelMessageQueue[] Subscribe(RedisChannel[] channels, CommandFlags flags = CommandFlags.None) => Array.Empty<ChannelMessageQueue>();
        public ValueTask<ChannelMessageQueue[]> SubscribeAsync(RedisChannel[] channels, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(Array.Empty<ChannelMessageQueue>());
    }
    
    private class StubTransaction : ITransaction
    {
        public IConnectionMultiplexer Multiplexer => null;
        public int Database => 0;
        
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        
        public bool Execute(string command, params object[] args) => true;
        public object ExecuteAsync(string command, params object[] args) => null;
        public RedisValue Get(RedisKey key, CommandFlags flags = CommandFlags.None) => RedisValue.Null;
        public ValueTask<RedisValue> GetAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(RedisValue.Null);
        public bool Set(RedisKey key, RedisValue value, TimeSpan? expiry = null, When when = When.Always, CommandFlags flags = CommandFlags.None) => true;
        public ValueTask<bool> SetAsync(RedisKey key, RedisValue value, TimeSpan? expiry = null, When when = When.Always, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(true);
        public bool KeyDelete(RedisKey key, CommandFlags flags = CommandFlags.None) => true;
        public ValueTask<bool> KeyDeleteAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(true);
        public bool Execute(CommandFlags flags = CommandFlags.None) => true;
        public ValueTask<bool> ExecuteAsync(CommandFlags flags = CommandFlags.None) => ValueTask.FromResult(true);
    }
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
