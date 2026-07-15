using MeuCatan.ClassLib.Contracts;

namespace MeuCatan.Api.Services;

public interface ILobbyRoomService
{
    LobbyJogosDisponiveisResponse ListarJogosDisponiveis();
    LobbyListarSalasResponse ListarSalas(int usuarioId);
    LobbyOperationResult<LobbyCriarSalaResponse> CriarSala(int usuarioId, string usuarioNome, bool isGuest, LobbyCriarSalaRequest request);
    LobbyOperationResult<LobbyDetalheSalaResponse> ObterSala(int salaId, int usuarioId);
    LobbyOperationResult<LobbyDetalheSalaResponse> EntrarSala(int salaId, int usuarioId, string usuarioNome, bool isGuest, string? codigoPrivado);
    LobbyOperationResult<LobbySairSalaResponse> SairSala(int salaId, int usuarioId);
    LobbyOperationResult<LobbyDetalheSalaResponse> SelecionarJogo(int salaId, int usuarioId, LobbySelecionarJogoRequest request);
    LobbyOperationResult<LobbyDetalheSalaResponse> AlterarPronto(int salaId, int usuarioId, bool isReady);
    LobbyOperationResult<LobbyIniciarJogoResponse> IniciarJogo(int salaId, int usuarioId);
}

public sealed class LobbyRoomService : ILobbyRoomService
{
    private readonly ILobbyGameCatalogService _gameCatalogService;
    private readonly IGameSessionService _gameSessionService;
    private readonly Lock _lock = new();
    private readonly Dictionary<int, SalaState> _salas = [];
    private int _ultimoSalaId;

    public LobbyRoomService(ILobbyGameCatalogService gameCatalogService, IGameSessionService gameSessionService)
    {
        _gameCatalogService = gameCatalogService;
        _gameSessionService = gameSessionService;
    }

    public LobbyJogosDisponiveisResponse ListarJogosDisponiveis()
    {
        return new LobbyJogosDisponiveisResponse
        {
            Jogos = _gameCatalogService.ListarJogos().ToList()
        };
    }

    public LobbyListarSalasResponse ListarSalas(int usuarioId)
    {
        lock (_lock)
        {
            var salas = _salas.Values
                .OrderByDescending(s => s.CriadaEmUtc)
                .Select(s => new LobbySalaResumoResponse
                {
                    SalaId = s.SalaId,
                    Nome = s.Nome,
                    Tipo = s.Tipo,
                    JogadoresAtuais = s.Jogadores.Count,
                    CapacidadeMaxima = s.CapacidadeMaxima,
                    RequerCodigo = s.Tipo == LobbyTipoSala.Privada,
                    CriadorNome = s.CriadorNome,
                    CriadaEmUtc = s.CriadaEmUtc,
                    UsuarioNaSala = s.Jogadores.ContainsKey(usuarioId),
                    GameType = s.GameType,
                    GameDisplayName = s.GameDisplayName,
                    Fase = s.Fase
                })
                .ToList();

            return new LobbyListarSalasResponse
            {
                Salas = salas
            };
        }
    }

    public LobbyOperationResult<LobbyCriarSalaResponse> CriarSala(int usuarioId, string usuarioNome, bool isGuest, LobbyCriarSalaRequest request)
    {
        if (isGuest)
        {
            return LobbyOperationResult<LobbyCriarSalaResponse>.Forbidden("Usuário convidado não pode criar sala.");
        }

        if (string.IsNullOrWhiteSpace(request.Nome))
        {
            return LobbyOperationResult<LobbyCriarSalaResponse>.Validation("O nome da sala é obrigatório.");
        }

        var jogo = ObterJogoOuErro(request.GameType);
        if (!jogo.Success)
        {
            return LobbyOperationResult<LobbyCriarSalaResponse>.Validation(jogo.ErrorMessage!);
        }

        if (request.CapacidadeMaxima < jogo.Data!.MinPlayers || request.CapacidadeMaxima > jogo.Data.MaxPlayers)
        {
            return LobbyOperationResult<LobbyCriarSalaResponse>.Validation($"A capacidade máxima para {jogo.Data.DisplayName} deve ficar entre {jogo.Data.MinPlayers} e {jogo.Data.MaxPlayers} jogadores.");
        }

        var tipo = request.Tipo.Trim().ToLowerInvariant();
        if (tipo is not (LobbyTipoSala.Publica or LobbyTipoSala.Privada))
        {
            return LobbyOperationResult<LobbyCriarSalaResponse>.Validation("Tipo de sala inválido. Use publica ou privada.");
        }

        var codigoPrivado = tipo == LobbyTipoSala.Privada
            ? NormalizarCodigoPrivado(request.CodigoPrivado)
            : null;

        lock (_lock)
        {
            var salaId = ++_ultimoSalaId;
            var sala = new SalaState
            {
                SalaId = salaId,
                Nome = request.Nome.Trim(),
                Tipo = tipo,
                CodigoPrivado = codigoPrivado,
                CriadorId = usuarioId,
                CriadorNome = usuarioNome,
                CapacidadeMaxima = request.CapacidadeMaxima,
                CriadaEmUtc = DateTime.UtcNow,
                GameType = jogo.Data.GameType,
                GameDisplayName = jogo.Data.DisplayName,
                MinJogadores = jogo.Data.MinPlayers,
                Fase = LobbyFaseSala.Lobby
            };

            sala.Jogadores.Add(usuarioId, new JogadorState
            {
                UsuarioId = usuarioId,
                Nome = usuarioNome,
                IsGuest = isGuest,
                IsReady = false,
                EntrouEmUtc = DateTime.UtcNow
            });

            _salas[salaId] = sala;

            return LobbyOperationResult<LobbyCriarSalaResponse>.Ok(new LobbyCriarSalaResponse
            {
                SalaId = salaId,
                Nome = sala.Nome,
                Tipo = sala.Tipo,
                CapacidadeMaxima = sala.CapacidadeMaxima,
                CodigoPrivadoGerado = sala.Tipo == LobbyTipoSala.Privada ? sala.CodigoPrivado : null,
                GameType = sala.GameType,
                Fase = sala.Fase
            });
        }
    }

    public LobbyOperationResult<LobbyDetalheSalaResponse> ObterSala(int salaId, int usuarioId)
    {
        lock (_lock)
        {
            if (!_salas.TryGetValue(salaId, out var sala))
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.NotFound("Sala não encontrada.");
            }

            return LobbyOperationResult<LobbyDetalheSalaResponse>.Ok(ToDetalheResponse(sala, usuarioId));
        }
    }

    public LobbyOperationResult<LobbyDetalheSalaResponse> EntrarSala(int salaId, int usuarioId, string usuarioNome, bool isGuest, string? codigoPrivado)
    {
        lock (_lock)
        {
            if (!_salas.TryGetValue(salaId, out var sala))
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.NotFound("Sala não encontrada.");
            }

            if (sala.Jogadores.ContainsKey(usuarioId))
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.Ok(ToDetalheResponse(sala, usuarioId));
            }

            if (sala.Fase is LobbyFaseSala.InGame or LobbyFaseSala.Ended)
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.Conflict("A sala não aceita novos jogadores neste momento.");
            }

            if (sala.Jogadores.Count >= sala.CapacidadeMaxima)
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.Conflict("Sala cheia.");
            }

            if (sala.Tipo == LobbyTipoSala.Privada)
            {
                var codigoNormalizado = NormalizarCodigoPrivado(codigoPrivado);
                if (!string.Equals(codigoNormalizado, sala.CodigoPrivado, StringComparison.OrdinalIgnoreCase))
                {
                    return LobbyOperationResult<LobbyDetalheSalaResponse>.Forbidden("Código da sala inválido.");
                }
            }

            sala.Jogadores.Add(usuarioId, new JogadorState
            {
                UsuarioId = usuarioId,
                Nome = usuarioNome,
                IsGuest = isGuest,
                IsReady = false,
                EntrouEmUtc = DateTime.UtcNow
            });

            sala.Fase = LobbyFaseSala.Setup;

            return LobbyOperationResult<LobbyDetalheSalaResponse>.Ok(ToDetalheResponse(sala, usuarioId));
        }
    }

    public LobbyOperationResult<LobbySairSalaResponse> SairSala(int salaId, int usuarioId)
    {
        lock (_lock)
        {
            if (!_salas.TryGetValue(salaId, out var sala))
            {
                return LobbyOperationResult<LobbySairSalaResponse>.NotFound("Sala não encontrada.");
            }

            if (!sala.Jogadores.Remove(usuarioId))
            {
                return LobbyOperationResult<LobbySairSalaResponse>.Conflict("Você não está nesta sala.");
            }

            if (sala.Jogadores.Count > 0 && sala.CriadorId == usuarioId)
            {
                var novoCriador = sala.Jogadores.Values
                    .OrderBy(j => j.EntrouEmUtc)
                    .First();

                sala.CriadorId = novoCriador.UsuarioId;
                sala.CriadorNome = novoCriador.Nome;
                novoCriador.IsReady = false;
            }

            if (sala.Jogadores.Count == 0)
            {
                _salas.Remove(salaId);
            }
            else if (sala.Fase == LobbyFaseSala.InGame)
            {
                sala.Fase = LobbyFaseSala.Setup;
            }

            return LobbyOperationResult<LobbySairSalaResponse>.Ok(new LobbySairSalaResponse
            {
                Success = true,
                Message = "Você saiu da sala."
            });
        }
    }

    public LobbyOperationResult<LobbyDetalheSalaResponse> SelecionarJogo(int salaId, int usuarioId, LobbySelecionarJogoRequest request)
    {
        lock (_lock)
        {
            if (!_salas.TryGetValue(salaId, out var sala))
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.NotFound("Sala não encontrada.");
            }

            if (sala.CriadorId != usuarioId)
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.Forbidden("Somente o criador da sala pode selecionar o jogo.");
            }

            if (sala.Fase is LobbyFaseSala.InGame or LobbyFaseSala.Ended)
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.Conflict("O jogo da sala não pode mais ser alterado.");
            }

            var jogo = ObterJogoOuErro(request.GameType);
            if (!jogo.Success)
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.Validation(jogo.ErrorMessage!);
            }

            if (sala.Jogadores.Count > jogo.Data!.MaxPlayers)
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.Conflict($"A sala possui mais jogadores do que o permitido para {jogo.Data.DisplayName}.");
            }

            sala.GameType = jogo.Data.GameType;
            sala.GameDisplayName = jogo.Data.DisplayName;
            sala.MinJogadores = jogo.Data.MinPlayers;

            if (sala.CapacidadeMaxima > jogo.Data.MaxPlayers)
            {
                sala.CapacidadeMaxima = jogo.Data.MaxPlayers;
            }

            sala.Fase = sala.Jogadores.Count > 1 ? LobbyFaseSala.Setup : LobbyFaseSala.Lobby;
            ResetarProntosNaoCriador(sala);

            return LobbyOperationResult<LobbyDetalheSalaResponse>.Ok(ToDetalheResponse(sala, usuarioId));
        }
    }

    public LobbyOperationResult<LobbyDetalheSalaResponse> AlterarPronto(int salaId, int usuarioId, bool isReady)
    {
        lock (_lock)
        {
            if (!_salas.TryGetValue(salaId, out var sala))
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.NotFound("Sala não encontrada.");
            }

            if (!sala.Jogadores.TryGetValue(usuarioId, out var jogador))
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.Forbidden("Você não participa desta sala.");
            }

            if (sala.CriadorId == usuarioId)
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.Forbidden("O criador da sala não usa o status de pronto.");
            }

            if (sala.Fase is LobbyFaseSala.InGame or LobbyFaseSala.Ended)
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.Conflict("Não é possível alterar o status de pronto nesta fase da sala.");
            }

            jogador.IsReady = isReady;
            sala.Fase = LobbyFaseSala.Setup;

            return LobbyOperationResult<LobbyDetalheSalaResponse>.Ok(ToDetalheResponse(sala, usuarioId));
        }
    }

    public LobbyOperationResult<LobbyIniciarJogoResponse> IniciarJogo(int salaId, int usuarioId)
    {
        lock (_lock)
        {
            if (!_salas.TryGetValue(salaId, out var sala))
            {
                return LobbyOperationResult<LobbyIniciarJogoResponse>.NotFound("Sala não encontrada.");
            }

            if (sala.CriadorId != usuarioId)
            {
                return LobbyOperationResult<LobbyIniciarJogoResponse>.Forbidden("Somente o criador da sala pode iniciar o jogo.");
            }

            if (sala.Fase == LobbyFaseSala.InGame)
            {
                return LobbyOperationResult<LobbyIniciarJogoResponse>.Conflict("O jogo já foi iniciado.");
            }

            if (sala.Jogadores.Count < sala.MinJogadores)
            {
                return LobbyOperationResult<LobbyIniciarJogoResponse>.Validation($"São necessários pelo menos {sala.MinJogadores} jogadores para iniciar esta partida.");
            }

            if (!TodosNaoCriadoresProntos(sala))
            {
                return LobbyOperationResult<LobbyIniciarJogoResponse>.Conflict("Todos os jogadores que não são o criador devem estar prontos.");
            }

            var gameSessionResult = _gameSessionService.CreateGameSessionFromRoom(new RoomGameStartContext
            {
                SalaId = sala.SalaId,
                GameType = sala.GameType,
                CriadorId = sala.CriadorId,
                Players = sala.Jogadores.Values
                    .OrderBy(j => j.EntrouEmUtc)
                    .Select(j => new RoomGameStartPlayer
                    {
                        UsuarioId = j.UsuarioId,
                        Nome = j.Nome,
                        JoinedAtUtc = j.EntrouEmUtc
                    })
                    .ToList()
            });

            if (!gameSessionResult.Success)
            {
                return LobbyOperationResult<LobbyIniciarJogoResponse>.Validation(gameSessionResult.ErrorMessage ?? "Não foi possível iniciar a sessão do jogo.");
            }

            sala.Fase = LobbyFaseSala.InGame;

            return LobbyOperationResult<LobbyIniciarJogoResponse>.Ok(new LobbyIniciarJogoResponse
            {
                SalaId = sala.SalaId,
                Fase = sala.Fase,
                RedirectPath = $"/jogo/{sala.SalaId}"
            });
        }
    }

    private static LobbyDetalheSalaResponse ToDetalheResponse(SalaState sala, int usuarioId)
    {
        var currentUser = sala.Jogadores.GetValueOrDefault(usuarioId);
        var currentUserIsCreator = sala.CriadorId == usuarioId;
        var allPlayersReady = TodosNaoCriadoresProntos(sala);

        return new LobbyDetalheSalaResponse
        {
            SalaId = sala.SalaId,
            Nome = sala.Nome,
            Tipo = sala.Tipo,
            CriadorId = sala.CriadorId,
            CriadorNome = sala.CriadorNome,
            CapacidadeMaxima = sala.CapacidadeMaxima,
            JogadoresAtuais = sala.Jogadores.Count,
            CriadaEmUtc = sala.CriadaEmUtc,
            GameType = sala.GameType,
            GameDisplayName = sala.GameDisplayName,
            Fase = sala.Fase,
            CurrentUserIsCreator = currentUserIsCreator,
            CurrentUserIsReady = currentUser?.IsReady ?? false,
            CanCurrentUserSelectGame = currentUserIsCreator && sala.Fase is LobbyFaseSala.Lobby or LobbyFaseSala.Setup,
            CanCurrentUserStart = currentUserIsCreator
                && sala.Fase is LobbyFaseSala.Lobby or LobbyFaseSala.Setup
                && allPlayersReady
                && sala.Jogadores.Count >= sala.MinJogadores,
            AllPlayersReady = allPlayersReady,
            MinJogadores = sala.MinJogadores,
            Jogadores = sala.Jogadores.Values
                .OrderBy(j => j.EntrouEmUtc)
                .Select(j => new LobbyJogadorResponse
                {
                    UsuarioId = j.UsuarioId,
                    Nome = j.Nome,
                    IsGuest = j.IsGuest,
                    IsCreator = j.UsuarioId == sala.CriadorId,
                    IsReady = j.IsReady
                })
                .ToList()
        };
    }

    private LobbyOperationResult<LobbyJogoDisponivelResponse> ObterJogoOuErro(string? gameType)
    {
        var jogo = _gameCatalogService.ObterJogo(gameType ?? string.Empty);
        if (jogo is null)
        {
            return LobbyOperationResult<LobbyJogoDisponivelResponse>.Validation("Tipo de jogo inválido.");
        }

        return LobbyOperationResult<LobbyJogoDisponivelResponse>.Ok(jogo);
    }

    private static bool TodosNaoCriadoresProntos(SalaState sala)
    {
        return sala.Jogadores.Values
            .Where(j => j.UsuarioId != sala.CriadorId)
            .All(j => j.IsReady);
    }

    private static void ResetarProntosNaoCriador(SalaState sala)
    {
        foreach (var jogador in sala.Jogadores.Values)
        {
            if (jogador.UsuarioId != sala.CriadorId)
            {
                jogador.IsReady = false;
            }
        }
    }

    private static string? NormalizarCodigoPrivado(string? codigo)
    {
        var normalizado = string.IsNullOrWhiteSpace(codigo)
            ? GerarCodigoPrivado()
            : codigo.Trim().ToUpperInvariant();

        if (normalizado.Length > 12)
        {
            normalizado = normalizado[..12];
        }

        return normalizado;
    }

    private static string GerarCodigoPrivado()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 6)
            .Select(_ => chars[Random.Shared.Next(chars.Length)])
            .ToArray());
    }

    private sealed class SalaState
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
        public Dictionary<int, JogadorState> Jogadores { get; } = [];
    }

    private sealed class JogadorState
    {
        public int UsuarioId { get; set; }
        public string Nome { get; set; } = string.Empty;
        public bool IsGuest { get; set; }
        public bool IsReady { get; set; }
        public DateTime EntrouEmUtc { get; set; }
    }
}

public enum LobbyErrorType
{
    Validation,
    Forbidden,
    NotFound,
    Conflict
}

public sealed class LobbyOperationResult<T>
{
    private LobbyOperationResult(bool success, T? data, LobbyErrorType? errorType, string? errorMessage)
    {
        Success = success;
        Data = data;
        ErrorType = errorType;
        ErrorMessage = errorMessage;
    }

    public bool Success { get; }
    public T? Data { get; }
    public LobbyErrorType? ErrorType { get; }
    public string? ErrorMessage { get; }

    public static LobbyOperationResult<T> Ok(T data) => new(true, data, null, null);
    public static LobbyOperationResult<T> Validation(string message) => new(false, default, LobbyErrorType.Validation, message);
    public static LobbyOperationResult<T> Forbidden(string message) => new(false, default, LobbyErrorType.Forbidden, message);
    public static LobbyOperationResult<T> NotFound(string message) => new(false, default, LobbyErrorType.NotFound, message);
    public static LobbyOperationResult<T> Conflict(string message) => new(false, default, LobbyErrorType.Conflict, message);
}
