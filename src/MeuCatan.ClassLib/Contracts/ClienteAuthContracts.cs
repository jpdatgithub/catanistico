namespace MeuCatan.ClassLib.Contracts;

public class LoginClientRequest
{
    public string Email { get; set; } = string.Empty;
    public string Senha { get; set; } = string.Empty;
}

public class RegisterClientRequest
{
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Senha { get; set; } = string.Empty;
}

public class GuestClientRequest
{
    public string? Nome { get; set; }
}

public class LoginClientResponse
{
    public string Token { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsGuest { get; set; }
}

public class ClientProfileResponse
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsGuest { get; set; }
}
