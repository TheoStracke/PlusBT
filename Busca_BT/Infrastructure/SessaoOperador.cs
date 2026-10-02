using Busca_BT.Models;

namespace Busca_BT.Infrastructure;

/// <summary>Quem está operando o sistema neste PC agora.</summary>
public interface ISessaoOperador
{
    Operador? Atual { get; }
    bool IsAdmin { get; }

    /// <summary>Nome deste computador, gravado junto com cada evento.</summary>
    string Pc { get; }

    void Entrar(Operador operador);
    void Sair();

    event Action? Mudou;
}

public sealed class SessaoOperador : ISessaoOperador
{
    public Operador? Atual { get; private set; }
    public bool IsAdmin => Atual?.IsAdmin == true;
    public string Pc { get; } = Environment.MachineName;

    public event Action? Mudou;

    public void Entrar(Operador operador)
    {
        Atual = operador;
        Mudou?.Invoke();
    }

    public void Sair()
    {
        Atual = null;
        Mudou?.Invoke();
    }
}
