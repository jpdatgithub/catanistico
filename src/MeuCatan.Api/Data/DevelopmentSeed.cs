using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MeuCatan.Api.Models;

namespace MeuCatan.Api.Data;

public static class DevelopmentSeed
{
    private const string TestName = "Teste";
    private const string TestEmail = "teste@gmail.com";
    private const string TestPassword = "Teste123@";

    public static async Task EnsureSeededAsync(AppDbContext context)
    {
        var emailNormalizado = TestEmail.Trim().ToLowerInvariant();

        var exists = await context.ClientUsers.AnyAsync(u => u.Email == emailNormalizado);
        if (exists)
        {
            return;
        }

        var user = new ClientUser
        {
            Nome = TestName,
            UserName = emailNormalizado,
            NormalizedUserName = emailNormalizado.ToUpperInvariant(),
            Email = emailNormalizado,
            NormalizedEmail = emailNormalizado.ToUpperInvariant(),
            EmailConfirmed = true,
            DataCriacaoUtc = DateTime.UtcNow,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N")
        };

        var hasher = new PasswordHasher<ClientUser>();
        user.SenhaHash = hasher.HashPassword(user, TestPassword);

        context.ClientUsers.Add(user);
        await context.SaveChangesAsync();
    }
}
