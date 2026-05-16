using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;

namespace Busca_BT.Data;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    public string ConnectionString { get; init; } = string.Empty;
}

public interface IDbConnectionFactory
{
    Task<IDbConnection> OpenAsync(CancellationToken ct = default);
}

public sealed class SqlServerConnectionFactory(DatabaseOptions options) : IDbConnectionFactory
{
    private readonly string _connectionString = options.ConnectionString;

    public async Task<IDbConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}

public sealed class DatabaseInitializer
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(IDbConnectionFactory factory,
                               ILogger<DatabaseInitializer> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
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

            _logger.LogInformation("Conexão com SQL Server validada com sucesso.");
        }
        catch (SqlException ex)
        {
            _logger.LogCritical(ex, "Falha ao conectar no SQL Server.");
            throw;
        }
    }
}