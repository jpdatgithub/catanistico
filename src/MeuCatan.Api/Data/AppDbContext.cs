using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using MeuCatan.Api.Models;

namespace MeuCatan.Api.Data;

public class AppDbContext : IdentityDbContext<ClientUser, IdentityRole<int>, int>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<ClientUser> ClientUsers => Set<ClientUser>();
}