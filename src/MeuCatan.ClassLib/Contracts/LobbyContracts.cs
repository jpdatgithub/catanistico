namespace MeuCatan.ClassLib.Contracts;

public static class LobbyTipoSala
{
    public const string Publica = "publica";
    public const string Privada = "privada";
}

public static class LobbyTipoJogo
{
    public const string CatanBase = "catan-base";
}

public enum LobbyFaseSala
{
    Lobby = 0,
    Setup = 1,
    InGame = 2,
    Ended = 3
}

public class LobbyJogosDisponiveisResponse
{
    public List<LobbyJogoDisponivelResponse> Jogos { get; set; } = [];
}

public class LobbyJogoDisponivelResponse
{
    public string GameType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public bool SupportsOptions { get; set; }
}

public class LobbyListarSalasResponse
{
    public List<LobbySalaResumoResponse> Salas { get; set; } = [];
}

public class LobbySalaResumoResponse
{
    public int SalaId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Tipo { get; set; } = LobbyTipoSala.Publica;
    public int JogadoresAtuais { get; set; }
    public int CapacidadeMaxima { get; set; }
    public bool RequerCodigo { get; set; }
    public string CriadorNome { get; set; } = string.Empty;
    public DateTime CriadaEmUtc { get; set; }
    public bool UsuarioNaSala { get; set; }
    public string GameType { get; set; } = LobbyTipoJogo.CatanBase;
    public string GameDisplayName { get; set; } = string.Empty;
    public LobbyFaseSala Fase { get; set; } = LobbyFaseSala.Lobby;
}

public class LobbyCriarSalaRequest
{
    public string Nome { get; set; } = string.Empty;
    public string Tipo { get; set; } = LobbyTipoSala.Publica;
    public int CapacidadeMaxima { get; set; } = 4;
    public string? CodigoPrivado { get; set; }
    public string GameType { get; set; } = LobbyTipoJogo.CatanBase;
}

public class LobbyCriarSalaResponse
{
    public int SalaId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Tipo { get; set; } = LobbyTipoSala.Publica;
    public int CapacidadeMaxima { get; set; }
    public string? CodigoPrivadoGerado { get; set; }
    public string GameType { get; set; } = LobbyTipoJogo.CatanBase;
    public LobbyFaseSala Fase { get; set; } = LobbyFaseSala.Lobby;
}

public class LobbyEntrarSalaRequest
{
    public string? CodigoPrivado { get; set; }
}

public class LobbyDetalheSalaResponse
{
    public int SalaId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Tipo { get; set; } = LobbyTipoSala.Publica;
    public int CriadorId { get; set; }
    public string CriadorNome { get; set; } = string.Empty;
    public int CapacidadeMaxima { get; set; }
    public int JogadoresAtuais { get; set; }
    public DateTime CriadaEmUtc { get; set; }
    public string GameType { get; set; } = LobbyTipoJogo.CatanBase;
    public string GameDisplayName { get; set; } = string.Empty;
    public LobbyFaseSala Fase { get; set; } = LobbyFaseSala.Lobby;
    public bool CurrentUserIsCreator { get; set; }
    public bool CurrentUserIsReady { get; set; }
    public bool CanCurrentUserSelectGame { get; set; }
    public bool CanCurrentUserStart { get; set; }
    public bool AllPlayersReady { get; set; }
    public int MinJogadores { get; set; }
    public List<LobbyJogadorResponse> Jogadores { get; set; } = [];
}

public class LobbyJogadorResponse
{
    public int UsuarioId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public bool IsGuest { get; set; }
    public bool IsCreator { get; set; }
    public bool IsReady { get; set; }
}

public class LobbySairSalaResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class LobbySelecionarJogoRequest
{
    public string GameType { get; set; } = LobbyTipoJogo.CatanBase;
}

public class LobbyAlterarProntoRequest
{
    public bool IsReady { get; set; }
}

public class LobbyIniciarJogoResponse
{
    public int SalaId { get; set; }
    public LobbyFaseSala Fase { get; set; } = LobbyFaseSala.InGame;
    public string RedirectPath { get; set; } = string.Empty;
}
