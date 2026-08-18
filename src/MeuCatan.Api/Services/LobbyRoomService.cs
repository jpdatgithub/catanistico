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
    LobbyOperationResult<LobbyDetalheSalaResponse> AtualizarTimerOptions(int salaId, int usuarioId, LobbyAtualizarTimerOptionsRequest request);
    LobbyOperationResult<LobbyDetalheSalaResponse> AlterarPronto(int salaId, int usuarioId, bool isReady);
    LobbyOperationResult<LobbyIniciarJogoResponse> IniciarJogo(int salaId, int usuarioId);
    LobbyOperationResult<LobbyDetalheSalaResponse> UpdatePlayerPresence(int salaId, int usuarioId, bool inLobby);
    void ExecutarManutencaoPresenca();
}

public sealed class LobbyRoomService : ILobbyRoomService
{
    private static readonly TimeSpan PresenceOnlineWindow = TimeSpan.FromMinutes(5);
    private const string PresenceSourceRoom = "sala";
    private const string PresenceSourceLobby = "lobby";

    private readonly ILobbyGameCatalogService _gameCatalogService;
    private readonly IGameSessionService _gameSessionService;
    private readonly IGameStateEventPublisher _gameStateEventPublisher;
    private readonly ILobbyRoomStore _roomStore;

    public LobbyRoomService(
        ILobbyGameCatalogService gameCatalogService,
        IGameSessionService gameSessionService,
        IGameStateEventPublisher gameStateEventPublisher,
        ILobbyRoomStore roomStore)
    {
        _gameCatalogService = gameCatalogService;
        _gameSessionService = gameSessionService;
        _gameStateEventPublisher = gameStateEventPublisher;
        _roomStore = roomStore;
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
        return _roomStore.Write(store =>
        {
            foreach (var sala in store.Rooms.ToList())
            {
                MarkPlayersOfflineByInactivity(store, sala, DateTime.UtcNow);

                var salaAtualizada = store.GetRoomOrDefault(sala.SalaId);
                if (salaAtualizada is null)
                {
                    continue;
                }

                if (TryRemoveRoomWhenAllPlayersOffline(store, salaAtualizada))
                {
                    _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(sala.SalaId, "room-removed");
                }
            }

            var salas = store.Rooms
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
        });
    }

    public void ExecutarManutencaoPresenca()
    {
        _roomStore.Write(store =>
        {
            var nowUtc = DateTime.UtcNow;
            foreach (var sala in store.Rooms.ToList())
            {
                MarkPlayersOfflineByInactivity(store, sala, nowUtc);

                var salaAtualizada = store.GetRoomOrDefault(sala.SalaId);
                if (salaAtualizada is null)
                {
                    continue;
                }

                if (TryRemoveRoomWhenAllPlayersOffline(store, salaAtualizada))
                {
                    _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(sala.SalaId, "room-removed");
                }
            }

            return 0;
        });
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

        return _roomStore.Write(store =>
        {
            var salaAtualDoUsuario = TryGetUserRoom(store, usuarioId);
            if (salaAtualDoUsuario is not null)
            {
                RemoverJogadorDaSala(store, salaAtualDoUsuario, usuarioId);
            }

            var salaId = store.NextRoomId();
            var sala = new LobbyRoomState
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

            sala.Jogadores.Add(usuarioId, new LobbyPlayerState
            {
                UsuarioId = usuarioId,
                Nome = usuarioNome,
                IsGuest = isGuest,
                IsReady = false,
                EntrouEmUtc = DateTime.UtcNow,
                LastSeenUtc = DateTime.UtcNow,
                PresenceSource = PresenceSourceRoom
            });

            store.Save(sala);
            _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(sala.SalaId, "room-created");

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
        });
    }

    public LobbyOperationResult<LobbyDetalheSalaResponse> ObterSala(int salaId, int usuarioId)
    {
        return _roomStore.Write(store =>
        {
            var sala = store.GetRoomOrDefault(salaId);
            if (sala is null)
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.NotFound("Sala não encontrada.");
            }

            MarkPlayersOfflineByInactivity(store, sala, DateTime.UtcNow);

            sala = store.GetRoomOrDefault(salaId);
            if (sala is null)
            {
                _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(salaId, "room-removed");
                return LobbyOperationResult<LobbyDetalheSalaResponse>.NotFound("Sala não encontrada.");
            }

            if (TryRemoveRoomWhenAllPlayersOffline(store, sala))
            {
                _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(salaId, "room-removed");
                return LobbyOperationResult<LobbyDetalheSalaResponse>.NotFound("Sala não encontrada.");
            }

            if (sala.Jogadores.TryGetValue(usuarioId, out var jogador))
            {
                var wasDisconnected = !jogador.IsConnected;
                jogador.IsConnected = true;
                jogador.LastSeenUtc = DateTime.UtcNow;
                jogador.PresenceSource = PresenceSourceRoom;
                store.Save(sala);

                if (wasDisconnected && sala.Fase == LobbyFaseSala.InGame)
                {
                    _gameSessionService.SetPlayerConnection(salaId, usuarioId, true);
                    _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(salaId, "player-reconnected");
                }
            }

            return LobbyOperationResult<LobbyDetalheSalaResponse>.Ok(ToDetalheResponse(sala, usuarioId));
        });
    }

    public LobbyOperationResult<LobbyDetalheSalaResponse> EntrarSala(int salaId, int usuarioId, string usuarioNome, bool isGuest, string? codigoPrivado)
    {
        return _roomStore.Write(store =>
        {
            var salaAtualDoUsuario = TryGetUserRoom(store, usuarioId);

            var sala = store.GetRoomOrDefault(salaId);
            if (sala is null)
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.NotFound("Sala não encontrada.");
            }

            MarkPlayersOfflineByInactivity(store, sala, DateTime.UtcNow);

            sala = store.GetRoomOrDefault(salaId);
            if (sala is null)
            {
                _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(salaId, "room-removed");
                return LobbyOperationResult<LobbyDetalheSalaResponse>.NotFound("Sala não encontrada.");
            }

            if (TryRemoveRoomWhenAllPlayersOffline(store, sala))
            {
                _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(salaId, "room-removed");
                return LobbyOperationResult<LobbyDetalheSalaResponse>.NotFound("Sala não encontrada.");
            }

            if (sala.Jogadores.ContainsKey(usuarioId))
            {
                var wasDisconnected = !sala.Jogadores[usuarioId].IsConnected;
                sala.Jogadores[usuarioId].IsConnected = true;
                sala.Jogadores[usuarioId].LastSeenUtc = DateTime.UtcNow;
                sala.Jogadores[usuarioId].PresenceSource = PresenceSourceRoom;
                store.Save(sala);

                if (wasDisconnected && sala.Fase == LobbyFaseSala.InGame)
                {
                    _gameSessionService.SetPlayerConnection(salaId, usuarioId, true);
                    _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(salaId, "player-reconnected");
                }

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

            if (salaAtualDoUsuario is not null)
            {
                RemoverJogadorDaSala(store, salaAtualDoUsuario, usuarioId);
            }

            sala.Jogadores.Add(usuarioId, new LobbyPlayerState
            {
                UsuarioId = usuarioId,
                Nome = usuarioNome,
                IsGuest = isGuest,
                IsReady = false,
                EntrouEmUtc = DateTime.UtcNow,
                LastSeenUtc = DateTime.UtcNow,
                PresenceSource = PresenceSourceRoom
            });

            sala.Fase = LobbyFaseSala.Setup;

            store.Save(sala);
            _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(sala.SalaId, "player-joined");

            return LobbyOperationResult<LobbyDetalheSalaResponse>.Ok(ToDetalheResponse(sala, usuarioId));
        });
    }

    public LobbyOperationResult<LobbySairSalaResponse> SairSala(int salaId, int usuarioId)
    {
        return _roomStore.Write(store =>
        {
            var sala = store.GetRoomOrDefault(salaId);
            if (sala is null)
            {
                return LobbyOperationResult<LobbySairSalaResponse>.NotFound("Sala não encontrada.");
            }

            MarkPlayersOfflineByInactivity(store, sala, DateTime.UtcNow);

            sala = store.GetRoomOrDefault(salaId);
            if (sala is null)
            {
                _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(salaId, "room-removed");
                return LobbyOperationResult<LobbySairSalaResponse>.NotFound("Sala não encontrada.");
            }

            if (TryRemoveRoomWhenAllPlayersOffline(store, sala))
            {
                _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(salaId, "room-removed");
                return LobbyOperationResult<LobbySairSalaResponse>.NotFound("Sala não encontrada.");
            }

            if (!sala.Jogadores.TryGetValue(usuarioId, out var jogador))
            {
                return LobbyOperationResult<LobbySairSalaResponse>.Conflict("Você não está nesta sala.");
            }

            sala.Jogadores.Remove(usuarioId);

            if (sala.Fase == LobbyFaseSala.InGame)
            {
                _gameSessionService.RemovePlayerFromSession(salaId, usuarioId);
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
                store.Remove(salaId);
                _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(salaId, "room-removed");
            }
            else
            {
                store.Save(sala);
                _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(salaId, "player-left");
            }

            return LobbyOperationResult<LobbySairSalaResponse>.Ok(new LobbySairSalaResponse
            {
                Success = true,
                Message = "Você saiu da sala."
            });
        });
    }

    public LobbyOperationResult<LobbyDetalheSalaResponse> SelecionarJogo(int salaId, int usuarioId, LobbySelecionarJogoRequest request)
    {
        return _roomStore.Write(store =>
        {
            var sala = store.GetRoomOrDefault(salaId);
            if (sala is null)
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.NotFound("Sala não encontrada.");
            }

            MarkPlayersOfflineByInactivity(store, sala, DateTime.UtcNow);

            sala = store.GetRoomOrDefault(salaId);
            if (sala is null)
            {
                _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(salaId, "room-removed");
                return LobbyOperationResult<LobbyDetalheSalaResponse>.NotFound("Sala não encontrada.");
            }

            if (TryRemoveRoomWhenAllPlayersOffline(store, sala))
            {
                _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(salaId, "room-removed");
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

            store.Save(sala);
            _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(sala.SalaId, "game-selected");

            return LobbyOperationResult<LobbyDetalheSalaResponse>.Ok(ToDetalheResponse(sala, usuarioId));
        });
    }

    public LobbyOperationResult<LobbyDetalheSalaResponse> AlterarPronto(int salaId, int usuarioId, bool isReady)
    {
        return _roomStore.Write(store =>
        {
            var sala = store.GetRoomOrDefault(salaId);
            if (sala is null)
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.NotFound("Sala não encontrada.");
            }

            MarkPlayersOfflineByInactivity(store, sala, DateTime.UtcNow);

            sala = store.GetRoomOrDefault(salaId);
            if (sala is null)
            {
                _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(salaId, "room-removed");
                return LobbyOperationResult<LobbyDetalheSalaResponse>.NotFound("Sala não encontrada.");
            }

            if (TryRemoveRoomWhenAllPlayersOffline(store, sala))
            {
                _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(salaId, "room-removed");
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
            jogador.LastSeenUtc = DateTime.UtcNow;
            sala.Fase = LobbyFaseSala.Setup;
            store.Save(sala);
            _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(sala.SalaId, "ready-changed");

            return LobbyOperationResult<LobbyDetalheSalaResponse>.Ok(ToDetalheResponse(sala, usuarioId));
        });
    }

    public LobbyOperationResult<LobbyDetalheSalaResponse> AtualizarTimerOptions(
        int salaId,
        int usuarioId,
        LobbyAtualizarTimerOptionsRequest request)
    {
        if (request is null)
        {
            return LobbyOperationResult<LobbyDetalheSalaResponse>.Validation("As opções de timer são obrigatórias.");
        }

        var optionsError = ValidateTimerOptions(request);
        if (optionsError is not null)
        {
            return LobbyOperationResult<LobbyDetalheSalaResponse>.Validation(optionsError);
        }

        return _roomStore.Write(store =>
        {
            var sala = store.GetRoomOrDefault(salaId);
            if (sala is null)
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.NotFound("Sala não encontrada.");
            }

            if (sala.CriadorId != usuarioId)
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.Forbidden("Somente o criador da sala pode alterar os timers.");
            }

            if (sala.Fase is LobbyFaseSala.InGame or LobbyFaseSala.Ended)
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.Conflict("Os timers não podem ser alterados após o início do jogo.");
            }

            if (sala.TimerOptions.TurnSeconds == request.TurnSeconds
                && sala.TimerOptions.DiscardSeconds == request.DiscardSeconds
                && sala.TimerOptions.RobberPlacementSeconds == request.RobberPlacementSeconds
                && sala.TimerOptions.TradeBonusSeconds == request.TradeBonusSeconds
                && sala.TimerOptions.InitialSettlementSeconds == request.InitialSettlementSeconds
                && sala.TimerOptions.InitialRoadSeconds == request.InitialRoadSeconds)
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.Ok(ToDetalheResponse(sala, usuarioId));
            }

            sala.TimerOptions = new CatanTimerOptions
            {
                DiceRollSeconds = 5,
                InitialSettlementSeconds = request.InitialSettlementSeconds,
                InitialRoadSeconds = request.InitialRoadSeconds,
                TurnSeconds = request.TurnSeconds,
                DiscardSeconds = request.DiscardSeconds,
                RobberPlacementSeconds = request.RobberPlacementSeconds,
                TradeBonusSeconds = request.TradeBonusSeconds
            };
            ResetarProntosNaoCriador(sala);
            store.Save(sala);
            _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(sala.SalaId, "timer-options-changed");

            return LobbyOperationResult<LobbyDetalheSalaResponse>.Ok(ToDetalheResponse(sala, usuarioId));
        });
    }

    public LobbyOperationResult<LobbyIniciarJogoResponse> IniciarJogo(int salaId, int usuarioId)
    {
        return _roomStore.Write(store =>
        {
            var sala = store.GetRoomOrDefault(salaId);
            if (sala is null)
            {
                return LobbyOperationResult<LobbyIniciarJogoResponse>.NotFound("Sala não encontrada.");
            }

            MarkPlayersOfflineByInactivity(store, sala, DateTime.UtcNow);

            sala = store.GetRoomOrDefault(salaId);
            if (sala is null)
            {
                _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(salaId, "room-removed");
                return LobbyOperationResult<LobbyIniciarJogoResponse>.NotFound("Sala não encontrada.");
            }

            if (TryRemoveRoomWhenAllPlayersOffline(store, sala))
            {
                _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(salaId, "room-removed");
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
                TimerOptions = CloneTimerOptions(sala.TimerOptions),
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
            sala.GameStartedAtUtc = DateTime.UtcNow;
            store.Save(sala);
            _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(sala.SalaId, "game-started");

            return LobbyOperationResult<LobbyIniciarJogoResponse>.Ok(new LobbyIniciarJogoResponse
            {
                SalaId = sala.SalaId,
                Fase = sala.Fase,
                GameStartedAtUtc = sala.GameStartedAtUtc,
                RedirectPath = $"/jogo/{sala.SalaId}"
            });
        });
    }

    public LobbyOperationResult<LobbyDetalheSalaResponse> UpdatePlayerPresence(int salaId, int usuarioId, bool inLobby)
    {
        return _roomStore.Write(store =>
        {
            var sala = store.GetRoomOrDefault(salaId);
            if (sala is null)
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.NotFound("Sala não encontrada.");
            }

            MarkPlayersOfflineByInactivity(store, sala, DateTime.UtcNow);

            sala = store.GetRoomOrDefault(salaId);
            if (sala is null)
            {
                _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(salaId, "room-removed");
                return LobbyOperationResult<LobbyDetalheSalaResponse>.NotFound("Sala não encontrada.");
            }

            if (TryRemoveRoomWhenAllPlayersOffline(store, sala))
            {
                _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(salaId, "room-removed");
                return LobbyOperationResult<LobbyDetalheSalaResponse>.NotFound("Sala não encontrada.");
            }

            if (!sala.Jogadores.TryGetValue(usuarioId, out var jogador))
            {
                return LobbyOperationResult<LobbyDetalheSalaResponse>.Forbidden("Você não participa desta sala.");
            }

            var wasDisconnected = !jogador.IsConnected;
            jogador.IsConnected = true;
            jogador.LastSeenUtc = DateTime.UtcNow;
            jogador.PresenceSource = inLobby ? PresenceSourceLobby : PresenceSourceRoom;
            store.Save(sala);

            if (wasDisconnected && sala.Fase == LobbyFaseSala.InGame)
            {
                _gameSessionService.SetPlayerConnection(salaId, usuarioId, true);
                _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(salaId, "player-reconnected");
            }

            return LobbyOperationResult<LobbyDetalheSalaResponse>.Ok(ToDetalheResponse(sala, usuarioId));
        });
    }

    private static LobbyDetalheSalaResponse ToDetalheResponse(LobbyRoomState sala, int usuarioId)
    {
        var currentUser = sala.Jogadores.GetValueOrDefault(usuarioId);
        var currentUserIsInRoom = currentUser is not null;
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
            GameStartedAtUtc = sala.GameStartedAtUtc,
            TimerOptions = CloneTimerOptions(sala.TimerOptions),
            CurrentUserIsInRoom = currentUserIsInRoom,
            CurrentUserIsCreator = currentUserIsCreator,
            CurrentUserIsReady = currentUser?.IsReady ?? false,
            CanCurrentUserSelectGame = currentUserIsInRoom && currentUserIsCreator && sala.Fase is LobbyFaseSala.Lobby or LobbyFaseSala.Setup,
            CanCurrentUserStart = currentUserIsInRoom
                && currentUserIsCreator
                && sala.Fase is LobbyFaseSala.Lobby or LobbyFaseSala.Setup
                && allPlayersReady
                && sala.Jogadores.Count >= sala.MinJogadores,
            AllPlayersReady = allPlayersReady,
            MinJogadores = sala.MinJogadores,
            Jogadores = sala.Jogadores.Values
                .OrderBy(j => j.EntrouEmUtc)
                .Select(j =>
                {
                    var isOnline = IsPlayerOnline(j);
                    return new LobbyJogadorResponse
                    {
                        UsuarioId = j.UsuarioId,
                        Nome = j.Nome,
                        IsGuest = j.IsGuest,
                        IsCreator = j.UsuarioId == sala.CriadorId,
                        IsReady = j.IsReady,
                        LastSeenUtc = j.LastSeenUtc,
                        IsOnline = isOnline,
                        IsInLobby = isOnline && string.Equals(j.PresenceSource, PresenceSourceLobby, StringComparison.OrdinalIgnoreCase)
                    };
                })
                .ToList()
        };
    }

    private static string? ValidateTimerOptions(LobbyAtualizarTimerOptionsRequest request)
    {
        return request.TurnSeconds is not (60 or 90 or 120)
            || request.InitialSettlementSeconds is not (60 or 90 or 120)
            || request.InitialRoadSeconds is not (15 or 30 or 45)
            || request.DiscardSeconds is not (60 or 90 or 120)
            || request.RobberPlacementSeconds is not (30 or 60)
            || request.TradeBonusSeconds is not (0 or 5 or 10 or 15 or 20)
                ? "Uma ou mais opções de timer são inválidas."
                : null;
    }

    private static CatanTimerOptions CloneTimerOptions(CatanTimerOptions options)
    {
        return new CatanTimerOptions
        {
            DiceRollSeconds = 5,
            InitialSettlementSeconds = options.InitialSettlementSeconds,
            InitialRoadSeconds = options.InitialRoadSeconds,
            TurnSeconds = options.TurnSeconds,
            DiscardSeconds = options.DiscardSeconds,
            RobberPlacementSeconds = options.RobberPlacementSeconds,
            TradeBonusSeconds = options.TradeBonusSeconds
        };
    }

    private void RemoveRoomAndGameSession(LobbyRoomStoreWriteContext store, LobbyRoomState sala)
    {
        store.Remove(sala.SalaId);
        _gameSessionService.DeleteSession(sala.SalaId);
    }

    private bool TryRemoveRoomWhenAllPlayersOffline(LobbyRoomStoreWriteContext store, LobbyRoomState sala)
    {
        if (sala.Jogadores.Count == 0)
        {
            RemoveRoomAndGameSession(store, sala);
            return true;
        }

        if (sala.Jogadores.Values.Any(IsPlayerOnline))
        {
            return false;
        }

        RemoveRoomAndGameSession(store, sala);
        return true;
    }

    private static bool IsPlayerOnline(LobbyPlayerState jogador)
    {
        return jogador.IsConnected;
    }

    private void MarkPlayersOfflineByInactivity(LobbyRoomStoreWriteContext store, LobbyRoomState sala, DateTime nowUtc)
    {
        var stalePlayers = sala.Jogadores.Values
            .Where(j => j.IsConnected && nowUtc - j.LastSeenUtc > PresenceOnlineWindow)
            .ToList();

        if (stalePlayers.Count == 0)
        {
            return;
        }

        foreach (var player in stalePlayers)
        {
            player.IsConnected = false;
            if (sala.Fase == LobbyFaseSala.InGame)
            {
                _gameSessionService.SetPlayerConnection(sala.SalaId, player.UsuarioId, false);
            }

            _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(sala.SalaId, "player-disconnected");
        }

        store.Save(sala);
    }

    private static LobbyRoomState? TryGetUserRoom(LobbyRoomStoreWriteContext store, int usuarioId)
    {
        return store.Rooms.FirstOrDefault(room => room.Jogadores.ContainsKey(usuarioId));
    }

    private void RemoverJogadorDaSala(LobbyRoomStoreWriteContext store, LobbyRoomState sala, int usuarioId)
    {
        if (!sala.Jogadores.Remove(usuarioId))
        {
            return;
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
            store.Remove(sala.SalaId);
            if (sala.Fase == LobbyFaseSala.InGame)
            {
                _gameSessionService.DeleteSession(sala.SalaId);
            }

            _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(sala.SalaId, "room-removed");
            return;
        }

        if (sala.Fase == LobbyFaseSala.InGame)
        {
            _gameSessionService.RemovePlayerFromSession(sala.SalaId, usuarioId);
        }

        store.Save(sala);
        _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(sala.SalaId, "player-left");
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

    private static bool TodosNaoCriadoresProntos(LobbyRoomState sala)
    {
        return sala.Jogadores.Values
            .Where(j => j.UsuarioId != sala.CriadorId)
            .All(j => j.IsReady);
    }

    private static void ResetarProntosNaoCriador(LobbyRoomState sala)
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
