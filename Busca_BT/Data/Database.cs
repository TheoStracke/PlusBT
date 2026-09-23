using Dapper;
using Microsoft.Data.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;

namespace Busca_BT.Data;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>Connection string configurada pelo usuário (tela de Configurações) ou appsettings.json (fallback inicial).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Quando true, tenta localizar o servidor automaticamente na rede local se a conexão configurada falhar.</summary>
    public bool AutoDiscover { get; set; } = true;

    /// <summary>Connection string efetivamente usada nas conexões. Pode ser substituída pela descoberta automática.</summary>
    public string ActiveConnectionString { get; set; } = string.Empty;
}

public interface IDbConnectionFactory
{
    Task<IDbConnection> OpenAsync(CancellationToken ct = default);
}

public sealed class SqlServerConnectionFactory(DatabaseOptions options) : IDbConnectionFactory
{
    public async Task<IDbConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new SqlConnection(options.ActiveConnectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}

// ────────────────────────────────────────────────────────────────────────────
// Descoberta automática de servidor SQL na rede local
// ────────────────────────────────────────────────────────────────────────────

public interface ISqlServerDiscoveryService
{
    /// <summary>
    /// Varre a rede local (SQL Server Browser / UDP 1434) procurando um servidor
    /// com a mesma instância da connection string modelo e testa a conexão em
    /// cada candidato encontrado. Retorna a primeira connection string que
    /// conseguir conectar, ou null se nenhuma funcionar.
    /// </summary>
    Task<string?> TryFindReachableServerAsync(string templateConnectionString, CancellationToken ct = default);
}

public sealed partial class SqlServerDiscoveryService(ILogger<SqlServerDiscoveryService> logger) : ISqlServerDiscoveryService
{
    public async Task<string?> TryFindReachableServerAsync(string templateConnectionString, CancellationToken ct = default)
    {
        var template = new SqlConnectionStringBuilder(templateConnectionString);
        var originalDataSource = template.DataSource;
        var instanceName = ExtractInstanceName(originalDataSource);

        LogScanStarting(logger, instanceName);

        DataTable sources;
        try
        {
            sources = await Task.Run(() => SqlDataSourceEnumerator.Instance.GetDataSources(), ct);
        }
        catch (Exception ex)
        {
            LogScanFailed(logger, ex);
            return null;
        }

        foreach (DataRow row in sources.Rows)
        {
            ct.ThrowIfCancellationRequested();

            if (row["ServerName"] is not string serverName || string.IsNullOrWhiteSpace(serverName))
                continue;

            var rowInstance = row["InstanceName"] as string ?? string.Empty;

            if (!string.IsNullOrEmpty(instanceName) &&
                !string.Equals(rowInstance, instanceName, StringComparison.OrdinalIgnoreCase))
                continue;

            var candidateDataSource = string.IsNullOrEmpty(rowInstance)
                ? serverName
                : $"{serverName}\\{rowInstance}";

            if (string.Equals(candidateDataSource, originalDataSource, StringComparison.OrdinalIgnoreCase))
                continue; // já sabemos que este falhou (é o que está configurado)

            var candidate = new SqlConnectionStringBuilder(templateConnectionString)
            {
                DataSource = candidateDataSource,
                ConnectTimeout = 3
            };

            if (await CanConnectAsync(candidate.ConnectionString, ct))
            {
                LogServerFound(logger, candidateDataSource);
                return candidate.ConnectionString;
            }
        }

        LogNoServerFound(logger);
        return null;
    }

    private static async Task<bool> CanConnectAsync(string connectionString, CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ExtractInstanceName(string dataSource)
    {
        var idx = dataSource.IndexOf('\\');
        return idx >= 0 ? dataSource[(idx + 1)..] : string.Empty;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Procurando servidores SQL na rede local (instância esperada: '{InstanceName}')...")]
    private static partial void LogScanStarting(ILogger logger, string instanceName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao varrer a rede local em busca de servidores SQL.")]
    private static partial void LogScanFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Servidor SQL encontrado na rede local: {DataSource}")]
    private static partial void LogServerFound(ILogger logger, string dataSource);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nenhum servidor SQL compatível foi encontrado na rede local.")]
    private static partial void LogNoServerFound(ILogger logger);
}

// ────────────────────────────────────────────────────────────────────────────
// Inicialização / validação do banco
// ────────────────────────────────────────────────────────────────────────────

public sealed class DatabaseInitializer
{
    private readonly IDbConnectionFactory _factory;
    private readonly DatabaseOptions _options;
    private readonly ISqlServerDiscoveryService _discovery;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        IDbConnectionFactory factory,
        DatabaseOptions options,
        ISqlServerDiscoveryService discovery,
        ILogger<DatabaseInitializer> logger)
    {
        _factory = factory;
        _options = options;
        _discovery = discovery;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            await ValidateSchemaAsync(ct);
        }
        catch (SqlException ex) when (_options.AutoDiscover)
        {
            _logger.LogWarning(ex,
                "Não foi possível conectar em {DataSource}. Procurando o servidor na rede local...",
                new SqlConnectionStringBuilder(_options.ActiveConnectionString).DataSource);

            var discovered = await _discovery.TryFindReachableServerAsync(_options.ConnectionString, ct);
            if (discovered is null)
            {
                _logger.LogCritical(ex, "Falha ao conectar no SQL Server (descoberta automática também falhou).");
                throw;
            }

            _options.ActiveConnectionString = discovered;
            await ValidateSchemaAsync(ct);
        }
        catch (SqlException ex)
        {
            _logger.LogCritical(ex, "Falha ao conectar no SQL Server.");
            throw;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogCritical(ex, "Falha inesperada ao validar o banco de dados.");
            throw;
        }
    }

    // Colunas que o app espera existir, mas podem faltar em um banco criado antes
    // dessa versão. Verificadas e criadas automaticamente a cada startup — evita
    // precisar rodar ALTER TABLE manualmente em cada servidor.
    private static readonly (string Table, string Column, string Definition)[] RequiredColumns =
    [
        ("Labels", "AbertaEm", "DATETIME NULL"),
    ];

    private async Task ValidateSchemaAsync(CancellationToken ct)
    {
        await using var conn = (SqlConnection)await _factory.OpenAsync(ct);

        var tables = await conn.QueryAsync<string>("""
            SELECT TABLE_NAME
            FROM   INFORMATION_SCHEMA.TABLES
            WHERE  TABLE_TYPE = 'BASE TABLE'
              AND  TABLE_NAME IN ('Labels', 'ImportBatches')
            """);

        var found = tables.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!found.Contains("Labels") || !found.Contains("ImportBatches"))
            throw new InvalidOperationException(
                "Tabelas não encontradas. Execute o script de schema no servidor.");

        await EnsureColumnsExistAsync(conn, ct);

        _logger.LogInformation("Conexão com SQL Server validada com sucesso. Servidor: {DataSource}",
            new SqlConnectionStringBuilder(_options.ActiveConnectionString).DataSource);
    }

    private async Task EnsureColumnsExistAsync(SqlConnection conn, CancellationToken ct)
    {
        foreach (var (table, column, definition) in RequiredColumns)
        {
            try
            {
                var exists = await conn.ExecuteScalarAsync<int>("""
                    SELECT COUNT(*) FROM sys.columns
                    WHERE object_id = OBJECT_ID(@FullTable) AND name = @Column
                    """,
                    new { FullTable = $"dbo.{table}", Column = column });

                if (exists > 0)
                    continue;

                _logger.LogWarning(
                    "Coluna {Table}.{Column} não encontrada no banco — criando automaticamente.",
                    table, column);

                await conn.ExecuteAsync($"ALTER TABLE dbo.{table} ADD {column} {definition};");
            }
            catch (Exception ex)
            {
                // Não bloqueia o startup: se faltar permissão de ALTER, o app segue
                // funcionando (só a funcionalidade que depende dessa coluna falha).
                _logger.LogError(ex,
                    "Falha ao criar coluna {Table}.{Column} automaticamente. " +
                    "Se o usuário/login não tiver permissão de ALTER TABLE, crie manualmente.",
                    table, column);
            }
        }
    }
}
