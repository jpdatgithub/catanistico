using System.Security.Claims;
using MeuCatan.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MeuCatan.Api.Hubs;

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class GameChatHub : Hub
{
    private readonly IGameChatService _gameChatService;

    public GameChatHub(IGameChatService gameChatService)
    {
        _gameChatService = gameChatService;
    }

    public async Task JoinGameRoom(int salaId)
    {
        var userContext = GetUserContext();
        if (userContext is null)
        {
            throw new HubException("Usuário não autenticado.");
        }

        var historyResult = _gameChatService.GetRecentMessages(salaId, userContext.UsuarioId);
        if (!historyResult.Success || historyResult.Data is null)
        {
            throw new HubException(historyResult.ErrorMessage ?? "Você não pode acessar este chat.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, BuildGroupName(salaId));
        await Clients.Caller.SendAsync("ReceiveChatHistory", historyResult.Data);
    }

    public async Task SendMessage(int salaId, string mensagem)
    {
        var userContext = GetUserContext();
        if (userContext is null)
        {
            throw new HubException("Usuário não autenticado.");
        }

        var messageResult = _gameChatService.AddMessage(salaId, userContext.UsuarioId, userContext.Nome, mensagem);
        if (!messageResult.Success || messageResult.Data is null)
        {
            throw new HubException(messageResult.ErrorMessage ?? "Não foi possível enviar a mensagem.");
        }

        await Clients.Group(BuildGroupName(salaId)).SendAsync("ReceiveChatMessage", messageResult.Data);
    }

    private static string BuildGroupName(int salaId)
    {
        return $"game-room-{salaId}";
    }

    private UserContext? GetUserContext()
    {
        var userIdClaim = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var nome = Context.User?.FindFirstValue(ClaimTypes.Name);

        if (!int.TryParse(userIdClaim, out var usuarioId) || string.IsNullOrWhiteSpace(nome))
        {
            return null;
        }

        return new UserContext
        {
            UsuarioId = usuarioId,
            Nome = nome
        };
    }

    private sealed class UserContext
    {
        public int UsuarioId { get; set; }
        public string Nome { get; set; } = string.Empty;
    }
}
