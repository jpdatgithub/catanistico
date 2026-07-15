using Microsoft.AspNetCore.Identity;

namespace MeuCatan.Api.Models
{
    public class ClientUser : IdentityUser<int>
    {
        public string Nome { get; set; } = string.Empty;
        public string SenhaHash { get; set; } = string.Empty;
        public DateTime DataCriacaoUtc { get; set; }
    }
}