using System.IO;
using System.Text.Json;

namespace Busca_BT.Infrastructure;

public enum ModoTemplates
{
    /// <summary>Templates vêm do acervo no banco, com cópia local sincronizada (padrão).</summary>
    Banco,
    /// <summary>Templates são abertos direto de uma pasta escolhida (contingência).</summary>
    PastaLocal
}

public sealed class PreferenciasPc
{
    public ModoTemplates ModoTemplates { get; set; } = ModoTemplates.Banco;

    /// <summary>Pasta usada no modo PastaLocal (ex.: \\Serveradeprint\arquivos\Etiquetas_PlusBT).</summary>
    public string PastaTemplates { get; set; } = string.Empty;
}

public interface IPreferenciasStore
{
    PreferenciasPc Atual { get; }
    void Salvar(PreferenciasPc preferencias);
    event Action? Mudou;
}

/// <summary>Preferências deste PC em %AppData%\BuscaBT\preferencias.json.</summary>
public sealed class PreferenciasStore : IPreferenciasStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _arquivo;

    public PreferenciasStore()
    {
        var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BuscaBT");
        Directory.CreateDirectory(pasta);
        _arquivo = Path.Combine(pasta, "preferencias.json");

        try
        {
            Atual = File.Exists(_arquivo)
                ? JsonSerializer.Deserialize<PreferenciasPc>(File.ReadAllText(_arquivo)) ?? new PreferenciasPc()
                : new PreferenciasPc();
        }
        catch
        {
            Atual = new PreferenciasPc();
        }
    }

    public PreferenciasPc Atual { get; private set; }

    public event Action? Mudou;

    public void Salvar(PreferenciasPc preferencias)
    {
        File.WriteAllText(_arquivo, JsonSerializer.Serialize(preferencias, JsonOptions));
        Atual = preferencias;
        Mudou?.Invoke();
    }
}
