using Busca_BT.Data;
using Busca_BT.Models;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Text.Json;

namespace Busca_BT.Infrastructure;

public interface IConnectionSettingsStore
{
    ConnectionSettings Load();
    void Save(ConnectionSettings settings);
}

/// <summary>
/// Persiste a configuração de conexão em %AppData%\BuscaBT\connection.settings.json —
/// fora da pasta do app, para que o usuário possa trocar servidor/rede sem editar
/// appsettings.json e recompilar. No primeiro uso, semeia com o valor de
/// appsettings.json (compilado) como ponto de partida.
/// </summary>
public sealed class ConnectionSettingsStore : IConnectionSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly string _seedConnectionString;
    private readonly bool _seedAutoDiscover;

    public ConnectionSettingsStore(IConfiguration configuration)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BuscaBT");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "connection.settings.json");

        var dbSection = configuration.GetSection(DatabaseOptions.Section);
        _seedConnectionString = dbSection["ConnectionString"] ?? string.Empty;
        _seedAutoDiscover = dbSection.GetValue("AutoDiscover", true);
    }

    public ConnectionSettings Load()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<ConnectionSettings>(json);
                if (settings is not null)
                    return settings;
            }
            catch
            {
                // Arquivo corrompido/ilegível: cai para a configuração inicial abaixo.
            }
        }

        var seeded = ConnectionSettings.FromConnectionString(_seedConnectionString);
        seeded.AutoDiscover = _seedAutoDiscover;
        return seeded;
    }

    public void Save(ConnectionSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_filePath, json);
    }
}
