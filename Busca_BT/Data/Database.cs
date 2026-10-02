using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Data;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Busca_BT.Data;

public sealed class DatabaseOptions
{
    /// <summary>Connection string do Postgres (Supabase), montada a partir das Configurações.</summary>
    public string ConnectionString { get; set; } = string.Empty;
}

public interface IDbConnectionFactory
{
    Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default);
}

public sealed class NpgsqlConnectionFactory(DatabaseOptions options) : IDbConnectionFactory
{
    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("Conexão com o banco não configurada. Preencha a tela Configurações.");

        var conn = new NpgsqlConnection(options.ConnectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}

// ────────────────────────────────────────────────────────────────────────────
// Inicialização: conecta e aplica as migrações pendentes
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Aplica, na ordem, os scripts Data/Migrations/NNN_*.sql (embutidos no .exe) que
/// ainda não constam em plusbt.schema_versao. Cada script roda numa transação: ou
/// entra inteiro ou nada. Um lock impede dois PCs de migrarem ao mesmo tempo.
/// </summary>
public sealed partial class DatabaseInitializer(
    IDbConnectionFactory factory,
    ILogger<DatabaseInitializer> logger)
{
    // Chave arbitrária do pg_advisory_lock usado durante a migração.
    private const long MigrationLockKey = 0x504C5553_4254; // "PLUSBT"

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = await factory.OpenAsync(ct);
            await MigrateAsync(conn, ct);
            LogConnected(logger, conn.Host ?? "?");
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or IOException)
        {
            LogConnectionFailed(logger, ex);
            throw;
        }
    }

    private async Task MigrateAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await conn.ExecuteAsync("select pg_advisory_lock(@Key);", new { Key = MigrationLockKey });
        try
        {
            await conn.ExecuteAsync("""
                create schema if not exists plusbt;
                create table if not exists plusbt.schema_versao (
                    versao      integer primary key,
                    aplicada_em timestamptz not null default now()
                );
                alter table plusbt.schema_versao enable row level security;
                """);

            var aplicadas = (await conn.QueryAsync<int>("select versao from plusbt.schema_versao;")).ToHashSet();

            foreach (var (versao, nome, sql) in LoadMigrations())
            {
                if (aplicadas.Contains(versao))
                    continue;

                LogApplyingMigration(logger, nome);
                await using var tx = await conn.BeginTransactionAsync(ct);
                await conn.ExecuteAsync(sql, transaction: tx);
                await conn.ExecuteAsync("insert into plusbt.schema_versao (versao) values (@Versao);",
                    new { Versao = versao }, tx);
                await tx.CommitAsync(ct);
            }
        }
        finally
        {
            await conn.ExecuteAsync("select pg_advisory_unlock(@Key);", new { Key = MigrationLockKey });
        }
    }

    private static IEnumerable<(int Versao, string Nome, string Sql)> LoadMigrations()
    {
        var assembly = Assembly.GetExecutingAssembly();

        return assembly.GetManifestResourceNames()
            .Select(name => (Name: name, Match: MigrationName().Match(name)))
            .Where(x => x.Match.Success)
            .Select(x =>
            {
                using var stream = assembly.GetManifestResourceStream(x.Name)!;
                using var reader = new StreamReader(stream);
                return (int.Parse(x.Match.Groups[1].Value), x.Match.Groups[0].Value, reader.ReadToEnd());
            })
            .OrderBy(m => m.Item1)
            .ToList();
    }

    [GeneratedRegex(@"Migrations\.(\d{3})_[\w-]+\.sql$")]
    private static partial Regex MigrationName();

    [LoggerMessage(Level = LogLevel.Information, Message = "Banco conectado e atualizado. Servidor: {Host}")]
    private static partial void LogConnected(ILogger logger, string host);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Aplicando migração do banco: {Nome}")]
    private static partial void LogApplyingMigration(ILogger logger, string nome);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Falha ao conectar no banco (Supabase).")]
    private static partial void LogConnectionFailed(ILogger logger, Exception ex);
}
