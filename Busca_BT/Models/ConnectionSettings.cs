using Microsoft.Data.SqlClient;

namespace Busca_BT.Models;

/// <summary>
/// Configuração de conexão com o SQL Server editável pelo usuário na tela de
/// Configurações, persistida por máquina (não faz parte do build/instalador).
/// </summary>
public sealed class ConnectionSettings
{
    public string Server { get; set; } = string.Empty;
    public string Instance { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;

    /// <summary>Quando true, usa a identidade do Windows do usuário logado (sem usuário/senha do SQL).</summary>
    public bool UseWindowsAuth { get; set; } = true;

    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool TrustServerCertificate { get; set; } = true;
    public int ConnectTimeoutSeconds { get; set; } = 10;
    public bool AutoDiscover { get; set; } = true;

    public string BuildConnectionString()
    {
        var dataSource = string.IsNullOrWhiteSpace(Instance) ? Server : $@"{Server}\{Instance}";

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = dataSource,
            InitialCatalog = Database,
            TrustServerCertificate = TrustServerCertificate,
            ConnectTimeout = ConnectTimeoutSeconds
        };

        if (UseWindowsAuth)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = UserId;
            builder.Password = Password;
        }

        return builder.ConnectionString;
    }

    public static ConnectionSettings FromConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return new ConnectionSettings();

        var builder = new SqlConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource ?? string.Empty;
        var idx = dataSource.IndexOf('\\');
        var server = idx >= 0 ? dataSource[..idx] : dataSource;
        var instance = idx >= 0 ? dataSource[(idx + 1)..] : string.Empty;

        return new ConnectionSettings
        {
            Server = server,
            Instance = instance,
            Database = builder.InitialCatalog,
            UseWindowsAuth = builder.IntegratedSecurity,
            UserId = builder.UserID,
            Password = builder.Password,
            TrustServerCertificate = builder.TrustServerCertificate,
            ConnectTimeoutSeconds = builder.ConnectTimeout
        };
    }
}
