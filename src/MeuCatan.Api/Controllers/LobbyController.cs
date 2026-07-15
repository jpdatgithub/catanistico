using System.Security.Claims;
using MeuCatan.Api.Services;
using MeuCatan.ClassLib.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeuCatan.Api.Controllers;

[ApiController]
[Route("api/lobby")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class LobbyController : ControllerBase
{
    private readonly ILobbyRoomService _lobbyRoomService;

    public LobbyController(ILobbyRoomService lobbyRoomService)
    {
        _lobbyRoomService = lobbyRoomService;
    }

    [HttpGet("salas")]
    public IActionResult ListarSalas()
    {
        var userContext = GetUserContext();
        if (userContext is null)
        {
            return Unauthorized();
        }

        var response = _lobbyRoomService.ListarSalas(userContext.UsuarioId);
        return Ok(response);
    }

    [HttpGet("jogos")]
    public IActionResult ListarJogos()
    {
        return Ok(_lobbyRoomService.ListarJogosDisponiveis());
    }

    [HttpPost("salas")]
    public IActionResult CriarSala([FromBody] LobbyCriarSalaRequest request)
    {
        var userContext = GetUserContext();
        if (userContext is null)
        {
            return Unauthorized();
        }

        var result = _lobbyRoomService.CriarSala(userContext.UsuarioId, userContext.Nome, userContext.IsGuest, request);
        return ToActionResult(result);
    }

    [HttpGet("salas/{salaId:int}")]
    public IActionResult ObterSala([FromRoute] int salaId)
    {
        var userContext = GetUserContext();
        if (userContext is null)
        {
            return Unauthorized();
        }

        var result = _lobbyRoomService.ObterSala(salaId, userContext.UsuarioId);
        return ToActionResult(result);
    }

    [HttpPost("salas/{salaId:int}/entrar")]
    public IActionResult EntrarSala([FromRoute] int salaId, [FromBody] LobbyEntrarSalaRequest request)
    {
        var userContext = GetUserContext();
        if (userContext is null)
        {
            return Unauthorized();
        }

        var result = _lobbyRoomService.EntrarSala(
            salaId,
            userContext.UsuarioId,
            userContext.Nome,
            userContext.IsGuest,
            request.CodigoPrivado);

        return ToActionResult(result);
    }

    [HttpPost("salas/{salaId:int}/sair")]
    public IActionResult SairSala([FromRoute] int salaId)
    {
        var userContext = GetUserContext();
        if (userContext is null)
        {
            return Unauthorized();
        }

        var result = _lobbyRoomService.SairSala(salaId, userContext.UsuarioId);
        return ToActionResult(result);
    }

    [HttpPost("salas/{salaId:int}/selecionar-jogo")]
    public IActionResult SelecionarJogo([FromRoute] int salaId, [FromBody] LobbySelecionarJogoRequest request)
    {
        var userContext = GetUserContext();
        if (userContext is null)
        {
            return Unauthorized();
        }

        var result = _lobbyRoomService.SelecionarJogo(salaId, userContext.UsuarioId, request);
        return ToActionResult(result);
    }

    [HttpPost("salas/{salaId:int}/pronto")]
    public IActionResult AlterarPronto([FromRoute] int salaId, [FromBody] LobbyAlterarProntoRequest request)
    {
        var userContext = GetUserContext();
        if (userContext is null)
        {
            return Unauthorized();
        }

        var result = _lobbyRoomService.AlterarPronto(salaId, userContext.UsuarioId, request.IsReady);
        return ToActionResult(result);
    }

    [HttpPost("salas/{salaId:int}/iniciar")]
    public IActionResult IniciarJogo([FromRoute] int salaId)
    {
        var userContext = GetUserContext();
        if (userContext is null)
        {
            return Unauthorized();
        }

        var result = _lobbyRoomService.IniciarJogo(salaId, userContext.UsuarioId);
        return ToActionResult(result);
    }

    private IActionResult ToActionResult<T>(LobbyOperationResult<T> result)
    {
        if (result.Success && result.Data is not null)
        {
            return Ok(result.Data);
        }

        var message = new
        {
            message = result.ErrorMessage ?? "Operação inválida."
        };

        return result.ErrorType switch
        {
            LobbyErrorType.Validation => BadRequest(message),
            LobbyErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, message),
            LobbyErrorType.NotFound => NotFound(message),
            LobbyErrorType.Conflict => Conflict(message),
            _ => BadRequest(message)
        };
    }

    private UserContext? GetUserContext()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var nome = User.FindFirstValue(ClaimTypes.Name);
        var tipo = User.FindFirstValue("cliente_tipo");

        if (!int.TryParse(userIdClaim, out var usuarioId) || string.IsNullOrWhiteSpace(nome))
        {
            return null;
        }

        return new UserContext
        {
            UsuarioId = usuarioId,
            Nome = nome,
            IsGuest = string.Equals(tipo, "convidado", StringComparison.OrdinalIgnoreCase)
        };
    }

    private sealed class UserContext
    {
        public int UsuarioId { get; set; }
        public string Nome { get; set; } = string.Empty;
        public bool IsGuest { get; set; }
    }
}
