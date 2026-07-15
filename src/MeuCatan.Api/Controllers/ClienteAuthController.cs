using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MeuCatan.ClassLib.Contracts;
using MeuCatan.Api.Models;
using MeuCatan.Api.Data;

namespace MeuCatan.Api.Controllers
{
    [ApiController]
    [Route("api/cliente-auth")]
    public class ClienteAuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly PasswordHasher<ClientUser> _passwordHasher = new();

        public ClienteAuthController(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] RegisterClientRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Nome)
                || string.IsNullOrWhiteSpace(request.Email)
                || string.IsNullOrWhiteSpace(request.Senha))
            {
                return BadRequest(new { message = "Nome, email e senha são obrigatórios." });
            }

            var emailNormalizado = request.Email.Trim().ToLowerInvariant();
            var emailEmUso = await _context.ClientUsers.AnyAsync(c => c.Email == emailNormalizado);
            if (emailEmUso)
            {
                return BadRequest(new { message = "Este email já está cadastrado." });
            }

            var cliente = new ClientUser
            {
                Nome = request.Nome.Trim(),
                Email = emailNormalizado,
                DataCriacaoUtc = DateTime.UtcNow
            };
            cliente.SenhaHash = _passwordHasher.HashPassword(cliente, request.Senha);

            _context.ClientUsers.Add(cliente);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Cadastro realizado com sucesso."
            });
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginClientRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Senha))
            {
                return BadRequest(new { message = "Email e senha são obrigatórios." });
            }

            var emailNormalizado = request.Email.Trim().ToLowerInvariant();
            var cliente = await _context.ClientUsers.FirstOrDefaultAsync(c => c.Email == emailNormalizado);
            if (cliente is null)
            {
                return Unauthorized(new { message = "Credenciais inválidas." });
            }

            var passwordVerification = _passwordHasher.VerifyHashedPassword(cliente, cliente.SenhaHash, request.Senha);
            if (passwordVerification == PasswordVerificationResult.Failed)
            {
                return Unauthorized(new { message = "Credenciais inválidas." });
            }

            var isGuest = IsGuest(cliente.Email);
            var token = GerarToken(cliente, isGuest);

            return Ok(new LoginClientResponse
            {
                Token = token,
                Nome = cliente.Nome,
                Email = cliente.Email ?? string.Empty,
                IsGuest = isGuest
            });
        }

        [HttpPost("guest")]
        [AllowAnonymous]
        public async Task<IActionResult> ContinueAsGuest([FromBody] GuestClientRequest? request)
        {
            var nome = string.IsNullOrWhiteSpace(request?.Nome)
                ? $"Convidado {Random.Shared.Next(1000, 9999)}"
                : request!.Nome!.Trim();

            var cliente = new ClientUser
            {
                Nome = nome,
                Email = $"guest-{Guid.NewGuid():N}@guest.restaurapp.local",
                DataCriacaoUtc = DateTime.UtcNow
            };
            cliente.SenhaHash = _passwordHasher.HashPassword(cliente, Guid.NewGuid().ToString("N"));

            _context.ClientUsers.Add(cliente);
            await _context.SaveChangesAsync();

            var token = GerarToken(cliente, isGuest: true);

            return Ok(new LoginClientResponse
            {
                Token = token,
                Nome = cliente.Nome,
                Email = cliente.Email ?? string.Empty,
                IsGuest = true
            });
        }

        [HttpGet("me")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> Me()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized();
            }

            var cliente = await _context.ClientUsers
                .Where(c => c.Id == userId)
                .Select(c => new ClientProfileResponse
                {
                    Id = c.Id,
                    Nome = c.Nome,
                    Email = c.Email ?? string.Empty,
                    IsGuest = IsGuest(c.Email)
                })
                .FirstOrDefaultAsync();

            if (cliente is null)
            {
                return Unauthorized();
            }

            return Ok(cliente);
        }

        private string GerarToken(ClientUser cliente, bool isGuest = false)
        {
            var jwtKey = _configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("Jwt:Key não configurada.");
            var jwtIssuer = _configuration["Jwt:Issuer"] ?? "Restaurapp.BlazorServer";
            var jwtAudience = _configuration["Jwt:Audience"] ?? "Restaurapp.ClienteWasm";

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, cliente.Id.ToString()),
                new(ClaimTypes.Name, cliente.Nome),
                new(ClaimTypes.Email, cliente.Email ?? string.Empty),
                new("cliente_tipo", isGuest ? "convidado" : "registrado")
            };

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddHours(12),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static bool IsGuest(string? email)
        {
            return !string.IsNullOrWhiteSpace(email)
                && email.EndsWith("@guest.restaurapp.local", StringComparison.OrdinalIgnoreCase);
        }
    }
}
