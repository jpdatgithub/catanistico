using System.Security.Claims;
using MeuCatan.Api.Services;
using MeuCatan.ClassLib.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeuCatan.Api.Controllers;

[ApiController]
[Route("api/jogo")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class GameController : ControllerBase
{
    private readonly IGameSessionService _gameSessionService;

    public GameController(IGameSessionService gameSessionService)
    {
        _gameSessionService = gameSessionService;
    }

    [HttpGet("{salaId:int}")]
    public IActionResult ObterSessao([FromRoute] int salaId)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var usuarioId))
        {
            return Unauthorized();
        }

        var result = _gameSessionService.GetSession(salaId, usuarioId);
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
}
