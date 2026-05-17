using Microsoft.AspNetCore.SignalR;

namespace Api.Hubs;

public class LogHub : Hub
{
    public async Task SubscribeToLogs(string serviceName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"logs-{serviceName}");
    }

    public async Task UnsubscribeFromLogs(string serviceName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"logs-{serviceName}");
    }
}
