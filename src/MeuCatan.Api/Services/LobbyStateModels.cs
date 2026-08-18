using MeuCatan.ClassLib.Contracts;

namespace MeuCatan.Api.Services;

public sealed class LobbyRoomState
{
    public int SalaId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Tipo { get; set; } = LobbyTipoSala.Publica;
    public string? CodigoPrivado { get; set; }
    public int CriadorId { get; set; }
    public string CriadorNome { get; set; } = string.Empty;
    public int CapacidadeMaxima { get; set; }
    public string GameType { get; set; } = LobbyTipoJogo.CatanBase;
    public string GameDisplayName { get; set; } = string.Empty;
    public int MinJogadores { get; set; }
    public LobbyFaseSala Fase { get; set; } = LobbyFaseSala.Lobby;
    public DateTime CriadaEmUtc { get; set; }
    public DateTime? GameStartedAtUtc { get; set; }
    public CatanTimerOptions TimerOptions { get; set; } = new();
    public Dictionary<int, LobbyPlayerState> Jogadores { get; set; } = [];
}

public sealed class LobbyPlayerState
{
    public int UsuarioId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public bool IsConnected { get; set; } = true;
    public bool IsGuest { get; set; }
    public bool IsReady { get; set; }
    public DateTime EntrouEmUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public string PresenceSource { get; set; } = "sala";
}