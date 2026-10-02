namespace Busca_BT.Models;

/// <summary>Pessoa que usa o sistema. Escolhida na tela "Quem está operando?".</summary>
public sealed class Operador
{
    public const string PerfilAdmin = "admin";
    public const string PerfilImpressao = "impressao";

    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;

    /// <summary>Hash do PIN (ver PinHasher). Null = entra sem PIN.</summary>
    public string? PinHash { get; set; }

    public string Perfil { get; set; } = PerfilImpressao;
    public bool Ativo { get; set; } = true;

    public bool TemPin => !string.IsNullOrEmpty(PinHash);
    public bool IsAdmin => Perfil == PerfilAdmin;
    public string PerfilTexto => IsAdmin ? "Administrador" : "Impressão";

    /// <summary>Iniciais para o avatar do cartão ("Gustavo" → "G", "Ana Paula" → "AP").</summary>
    public string Iniciais => string.Concat(
        Nome.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));
}
