using MeuCatan.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MeuCatan.Api.Services;

public interface IGameStateEventPublisher
{
    Task PublishGameStateInvalidationAsync(int salaId, string eventType);
}

public sealed class SignalRGameStateEventPublisher : IGameStateEventPublisher
{
    private readonly IHubContext<GameChatHub> _hubContext;

    public SignalRGameStateEventPublisher(IHubContext<GameChatHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task PublishGameStateInvalidationAsync(int salaId, string eventType)
    {
        return _hubContext.Clients
            .Group(BuildGroupName(salaId))
            .SendAsync("ReceiveGameStateInvalidation", salaId, eventType, DateTime.UtcNow);
    }

    private static string BuildGroupName(int salaId)
    {
        return $"game-room-{salaId}";
    }
}
