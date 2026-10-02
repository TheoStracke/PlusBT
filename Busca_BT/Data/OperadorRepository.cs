using Busca_BT.Models;
using Dapper;

namespace Busca_BT.Data;

public interface IOperadorRepository
{
    /// <summary>Todos os operadores (ativos primeiro, depois por nome).</summary>
    Task<IReadOnlyList<Operador>> ListarAsync(bool somenteAtivos);

    Task<Operador> CriarAsync(string nome, string perfil, string? pinHash);
    Task AtualizarAsync(Guid id, string nome, string perfil, bool ativo);

    /// <summary>Define (ou remove, com null) o PIN do operador.</summary>
    Task DefinirPinAsync(Guid id, string? pinHash);
}

public sealed class OperadorRepository(IDbConnectionFactory factory) : IOperadorRepository
{
    private const string SelectSql = """
        select id        as "Id",
               nome      as "Nome",
               pin_hash  as "PinHash",
               perfil    as "Perfil",
               ativo     as "Ativo"
        from   plusbt.operadores
        """;

    public async Task<IReadOnlyList<Operador>> ListarAsync(bool somenteAtivos)
    {
        await using var conn = await factory.OpenAsync();
        var where = somenteAtivos ? "where ativo" : string.Empty;
        var rows = await conn.QueryAsync<Operador>($"{SelectSql} {where} order by ativo desc, nome;");
        return rows.AsList();
    }

    public async Task<Operador> CriarAsync(string nome, string perfil, string? pinHash)
    {
        await using var conn = await factory.OpenAsync();
        return await conn.QuerySingleAsync<Operador>($"""
            with novo as (
                insert into plusbt.operadores (nome, perfil, pin_hash)
                values (@Nome, @Perfil, @PinHash)
                returning *
            )
            select id as "Id", nome as "Nome", pin_hash as "PinHash", perfil as "Perfil", ativo as "Ativo"
            from novo;
            """, new { Nome = nome, Perfil = perfil, PinHash = pinHash });
    }

    public async Task AtualizarAsync(Guid id, string nome, string perfil, bool ativo)
    {
        await using var conn = await factory.OpenAsync();
        await conn.ExecuteAsync(
            "update plusbt.operadores set nome = @Nome, perfil = @Perfil, ativo = @Ativo where id = @Id;",
            new { Id = id, Nome = nome, Perfil = perfil, Ativo = ativo });
    }

    public async Task DefinirPinAsync(Guid id, string? pinHash)
    {
        await using var conn = await factory.OpenAsync();
        await conn.ExecuteAsync(
            "update plusbt.operadores set pin_hash = @PinHash where id = @Id;",
            new { Id = id, PinHash = pinHash });
    }
}
