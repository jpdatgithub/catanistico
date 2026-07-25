using MeuCatan.ClassLib.Contracts;

namespace MeuCatan.Api.Services;

public interface IGameChatService
{
    LobbyOperationResult<IReadOnlyList<GameChatMessageResponse>> GetRecentMessages(int salaId, int usuarioId);
    LobbyOperationResult<GameChatMessageResponse> AddMessage(int salaId, int usuarioId, string usuarioNome, string mensagem);
}

public sealed class InMemoryGameChatService : IGameChatService
{
    private const int MaxMessagesPerRoom = 100;
    private const int MaxMessageLength = 500;

    private readonly Lock _lock = new();
    private readonly Dictionary<int, List<GameChatMessageResponse>> _messagesByRoom = [];
    private readonly IGameSessionService _gameSessionService;

    public InMemoryGameChatService(IGameSessionService gameSessionService)
    {
        _gameSessionService = gameSessionService;
    }

    public LobbyOperationResult<IReadOnlyList<GameChatMessageResponse>> GetRecentMessages(int salaId, int usuarioId)
    {
        var authorization = EnsurePlayerCanAccessRoom(salaId, usuarioId);
        if (!authorization.Success)
        {
            return LobbyOperationResult<IReadOnlyList<GameChatMessageResponse>>.Forbidden(
                authorization.ErrorMessage ?? "Você não participa desta sessão.");
        }

        lock (_lock)
        {
            var history = _messagesByRoom.TryGetValue(salaId, out var messages)
                ? messages.ToList()
                : [];

            return LobbyOperationResult<IReadOnlyList<GameChatMessageResponse>>.Ok(history);
        }
    }

    public LobbyOperationResult<GameChatMessageResponse> AddMessage(int salaId, int usuarioId, string usuarioNome, string mensagem)
    {
        var authorization = EnsurePlayerCanAccessRoom(salaId, usuarioId);
        if (!authorization.Success)
        {
            return LobbyOperationResult<GameChatMessageResponse>.Forbidden(
                authorization.ErrorMessage ?? "Você não participa desta sessão.");
        }

        var normalizedMessage = (mensagem ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            return LobbyOperationResult<GameChatMessageResponse>.Validation("A mensagem não pode ser vazia.");
        }

        if (normalizedMessage.Length > MaxMessageLength)
        {
            return LobbyOperationResult<GameChatMessageResponse>.Validation($"A mensagem deve ter no máximo {MaxMessageLength} caracteres.");
        }

        var message = new GameChatMessageResponse
        {
            SalaId = salaId,
            UsuarioId = usuarioId,
            UsuarioNome = usuarioNome,
            Mensagem = normalizedMessage,
            EnviadaEmUtc = DateTime.UtcNow
        };

        lock (_lock)
        {
            if (!_messagesByRoom.TryGetValue(salaId, out var messages))
            {
                messages = [];
                _messagesByRoom[salaId] = messages;
            }

            messages.Add(message);

            if (messages.Count > MaxMessagesPerRoom)
            {
                messages.RemoveAt(0);
            }
        }

        return LobbyOperationResult<GameChatMessageResponse>.Ok(message);
    }

    private LobbyOperationResult<GameSessionResponse> EnsurePlayerCanAccessRoom(int salaId, int usuarioId)
    {
        var sessionResult = _gameSessionService.GetSession(salaId, usuarioId);
        if (!sessionResult.Success)
        {
            return LobbyOperationResult<GameSessionResponse>.Forbidden(
                sessionResult.ErrorMessage ?? "Sessão de jogo não encontrada.");
        }

        return LobbyOperationResult<GameSessionResponse>.Ok(sessionResult.Data!);
    }
}
