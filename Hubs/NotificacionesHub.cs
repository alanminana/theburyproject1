using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace TheBuryProject.Hubs;

[Authorize]
public class NotificacionesHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var usuario = Context.User?.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(usuario))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, usuario);
        }

        await base.OnConnectedAsync();
    }
}
