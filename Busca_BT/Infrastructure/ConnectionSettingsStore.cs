using Busca_BT.Models;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Busca_BT.Infrastructure;

public interface IConnectionSettingsStore
{
    /// <summary>Conexão padrão embutida no .exe, se houver (null em builds sem ela).</summary>
    ConnectionSettings? Embutida { get; }

    ConnectionSettings Load();
    void Save(ConnectionSettings settings);
}

/// <summary>
/// Persiste a conexão com o Supabase em %AppData%\BuscaBT\supabase.settings.json.
/// A senha é gravada criptografada com DPAPI (só o mesmo usuário do Windows, no mesmo
/// PC, consegue ler). Se o arquivo ainda não existe, usa um .env (desenvolvimento:
/// procurado na pasta do .exe e nas pastas acima) ou as variáveis de ambiente SUPABASE_DB_*.
/// Sem nada disso (PC recém-instalado), usa a conexão padrão embutida no .exe pelo
/// publicar.ps1 (usuário plusbt_app, com acesso só ao schema plusbt).
/// </summary>
public sealed class ConnectionSettingsStore : IConnectionSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PlusBT.Supabase");

    // Chave do embaralhamento do conexao.bin (a mesma do publicar.ps1). Não é criptografia
    // de verdade: só evita a senha aparecer em texto puro dentro do .exe.
    private static readonly byte[] ChaveEmbutida = Encoding.UTF8.GetBytes("PlusBT.conexao.v1");

    private static readonly Lazy<ConnectionSettings?> _embutida = new(LerEmbutida);

    public ConnectionSettings? Embutida => _embutida.Value;

    private readonly string _filePath;

    public ConnectionSettingsStore()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BuscaBT");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "supabase.settings.json");
    }

    // Formato em disco: igual ao ConnectionSettings, mas com a senha criptografada.
    private sealed class Stored
    {
        public string Host { get; set; } = ConnectionSettings.DefaultHost;
        public int Port { get; set; } = 5432;
        public string Database { get; set; } = "postgres";
        public string Username { get; set; } = string.Empty;
        public string PasswordProtected { get; set; } = string.Empty;
    }

    public ConnectionSettings Load()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(_filePath));
                if (stored is not null)
                {
                    return new ConnectionSettings
                    {
                        Host = stored.Host,
                        Port = stored.Port,
                        Database = stored.Database,
                        Username = stored.Username,
                        Password = Unprotect(stored.PasswordProtected)
                    };
                }
            }
            catch
            {
                // Arquivo corrompido ou de outro usuário do Windows: cai no .env / variáveis abaixo.
            }
        }

        var doAmbiente = FromEnvironment();
        return doAmbiente.IsComplete ? doAmbiente : Embutida ?? doAmbiente;
    }

    public void Save(ConnectionSettings settings)
    {
        var stored = new Stored
        {
            Host = settings.Host,
            Port = settings.Port,
            Database = settings.Database,
            Username = settings.Username,
            PasswordProtected = Protect(settings.Password)
        };
        File.WriteAllText(_filePath, JsonSerializer.Serialize(stored, JsonOptions));
    }

    private static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    private static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    private static ConnectionSettings FromEnvironment()
    {
        var values = ReadDotEnv();

        string? Get(string key) =>
            Environment.GetEnvironmentVariable(key) is { Length: > 0 } fromEnv ? fromEnv
            : values.TryGetValue(key, out var fromFile) && fromFile.Length > 0 ? fromFile
            : null;

        var settings = new ConnectionSettings();
        settings.Host = Get("SUPABASE_DB_HOST") ?? settings.Host;
        settings.Port = int.TryParse(Get("SUPABASE_DB_PORT"), out var port) ? port : settings.Port;
        settings.Database = Get("SUPABASE_DB_NAME") ?? settings.Database;
        settings.Username = Get("SUPABASE_DB_USER") ?? settings.Username;
        settings.Password = Get("SUPABASE_DB_PASSWORD") ?? settings.Password;
        return settings;
    }

    private sealed record ConexaoEmbutida(string Host, int Port, string Database, string Username, string Password);

    private static ConnectionSettings? LerEmbutida()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Busca_BT.conexao.bin");
            if (stream is null)
                return null;

            using var reader = new StreamReader(stream);
            var bytes = Convert.FromBase64String(reader.ReadToEnd().Trim());
            for (var i = 0; i < bytes.Length; i++)
                bytes[i] ^= ChaveEmbutida[i % ChaveEmbutida.Length];

            var c = JsonSerializer.Deserialize<ConexaoEmbutida>(bytes);
            if (c is null)
                return null;

            return new ConnectionSettings
            {
                Host = c.Host,
                Port = c.Port,
                Database = c.Database,
                Username = c.Username,
                Password = c.Password,
                Embutida = true
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Lê o primeiro .env encontrado subindo a partir da pasta do .exe.</summary>
    private static Dictionary<string, string> ReadDotEnv()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ".env");
            if (!File.Exists(path))
                continue;

            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    continue;

                var idx = trimmed.IndexOf('=');
                if (idx <= 0)
                    continue;

                result[trimmed[..idx].Trim()] = trimmed[(idx + 1)..].Trim().Trim('"');
            }
            break;
        }

        return result;
    }
}
