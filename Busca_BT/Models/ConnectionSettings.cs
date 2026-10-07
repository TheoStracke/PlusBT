using Npgsql;

namespace Busca_BT.Models;

/// <summary>
/// Conexão com o Postgres do Supabase, editável na tela de Configurações e
/// salva por máquina (a senha vai criptografada, ver ConnectionSettingsStore).
/// </summary>
public sealed class ConnectionSettings
{
    public const string DefaultHost = "aws-0-sa-east-1.pooler.supabase.com";

    public string Host { get; set; } = DefaultHost;
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "postgres";

    /// <summary>No Session pooler do Supabase o usuário é "postgres.&lt;id-do-projeto&gt;".</summary>
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
    public int ConnectTimeoutSeconds { get; set; } = 15;

    /// <summary>True quando veio da conexão padrão embutida no .exe (não é gravada em disco).</summary>
    public bool Embutida { get; init; }

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrEmpty(Password);

    public string BuildConnectionString() => new NpgsqlConnectionStringBuilder
    {
        Host = Host,
        Port = Port,
        Database = Database,
        Username = Username,
        Password = Password,
        SslMode = SslMode.Require,
        Timeout = ConnectTimeoutSeconds,
        CommandTimeout = 60,
        SearchPath = "plusbt,public",
        // Internet instável: conexões ociosas são descartadas logo e recriadas sob demanda.
        ConnectionIdleLifetime = 60,
        KeepAlive = 30,
        ApplicationName = "PlusBT"
    }.ConnectionString;
}
