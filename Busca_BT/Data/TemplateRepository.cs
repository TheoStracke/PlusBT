using Busca_BT.Infrastructure;
using Busca_BT.Models;
using Dapper;

namespace Busca_BT.Data;

/// <summary>Conteúdo de uma versão baixado do banco.</summary>
public sealed record ConteudoTemplate(string Codigo, int Versao, string NomeArquivo, string Hash, byte[] Conteudo);

/// <summary>
/// Acervo de templates no Supabase (acesso direto, exige internet): lista, envio de
/// versões com hash e conflito, versões guardadas e restauração.
/// </summary>
public sealed class TemplateRepository(IDbConnectionFactory factory, ISessaoOperador sessao)
{
    /// <summary>Versões anteriores guardadas como backup, além da atual.</summary>
    public const int VersoesDeBackup = 3;

    public async Task<IReadOnlyList<TemplateInfo>> ListarAsync()
    {
        await using var conn = await factory.OpenAsync();
        var rows = await conn.QueryAsync<TemplateInfo>("""
            select t.id            as "Id",
                   t.codigo        as "Codigo",
                   t.nome_arquivo  as "NomeArquivo",
                   t.versao_atual  as "VersaoAtual",
                   t.hash_atual    as "HashAtual",
                   t.atualizado_em as "AtualizadoEm",
                   o.nome          as "AtualizadoPor"
            from   plusbt.templates t
            left   join plusbt.operadores o on o.id = t.atualizado_por
            order  by t.codigo;
            """);
        return rows.AsList();
    }

    /// <summary>Conteúdo da versão atual dos templates pedidos (por id), em lote.</summary>
    public async Task<IReadOnlyList<ConteudoTemplate>> BaixarAtuaisAsync(IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<ConteudoTemplate>(new CommandDefinition("""
            select t.codigo        as "Codigo",
                   v.versao        as "Versao",
                   v.nome_arquivo  as "NomeArquivo",
                   v.hash_sha256   as "Hash",
                   v.conteudo      as "Conteudo"
            from   plusbt.templates t
            join   plusbt.template_versoes v on v.template_id = t.id and v.versao = t.versao_atual
            where  t.id = any(@Ids);
            """, new { Ids = ids.ToArray() }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<TemplateVersao>> ListarVersoesAsync(string codigo)
    {
        await using var conn = await factory.OpenAsync();
        var rows = await conn.QueryAsync<TemplateVersao>("""
            select v.versao        as "Versao",
                   v.nome_arquivo  as "NomeArquivo",
                   v.hash_sha256   as "Hash",
                   v.tamanho       as "Tamanho",
                   v.origem        as "Origem",
                   v.enviado_em    as "EnviadoEm",
                   o.nome          as "EnviadoPor"
            from   plusbt.template_versoes v
            join   plusbt.templates t on t.id = v.template_id
            left   join plusbt.operadores o on o.id = v.enviado_por
            where  t.codigo = @Codigo
            order  by v.versao desc;
            """, new { Codigo = codigo });
        return rows.AsList();
    }

    public async Task<byte[]?> ConteudoDaVersaoAsync(string codigo, int versao)
    {
        await using var conn = await factory.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<byte[]>("""
            select v.conteudo
            from   plusbt.template_versoes v
            join   plusbt.templates t on t.id = v.template_id
            where  t.codigo = @Codigo and v.versao = @Versao;
            """, new { Codigo = codigo, Versao = versao });
    }

    /// <summary>
    /// Grava uma nova versão do template (cria o template se o código for novo).
    /// Arquivo idêntico à versão atual não gera versão. Com <paramref name="versaoBase"/>,
    /// recusa (Conflito) se alguém já tiver enviado uma versão depois dela, a menos que
    /// <paramref name="forcar"/>. Mantém a atual + <see cref="VersoesDeBackup"/> anteriores.
    /// </summary>
    public async Task<EnvioTemplate> EnviarAsync(
        string codigo, string nomeArquivo, byte[] conteudo, string origem,
        int? versaoBase = null, bool forcar = false, CancellationToken ct = default)
    {
        var hash = HashArquivo.Sha256(conteudo);

        await using var conn = await factory.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var atual = await conn.QuerySingleOrDefaultAsync<(int Id, int Versao, string? Hash, DateTime? Em, string? Por)>("""
            select t.id, t.versao_atual, t.hash_atual, t.atualizado_em, o.nome
            from   plusbt.templates t
            left   join plusbt.operadores o on o.id = t.atualizado_por
            where  t.codigo = @Codigo
            for    update of t;
            """, new { Codigo = codigo }, tx);

        var existe = atual.Id != 0;

        if (existe && atual.Hash == hash)
            return new EnvioTemplate(codigo, ResultadoEnvio.Identico, atual.Versao);

        if (existe && versaoBase is int baseV && atual.Versao > baseV && !forcar)
            return new EnvioTemplate(codigo, ResultadoEnvio.Conflito, atual.Versao, atual.Por, atual.Em);

        var operadorId = sessao.Atual?.Id;
        var novaVersao = existe ? atual.Versao + 1 : 1;

        var templateId = existe
            ? atual.Id
            : await conn.QuerySingleAsync<int>("""
                insert into plusbt.templates (codigo, nome_arquivo, versao_atual, hash_atual, atualizado_em, atualizado_por)
                values (@Codigo, @NomeArquivo, 0, null, now(), @OperadorId)
                returning id;
                """, new { Codigo = codigo, NomeArquivo = nomeArquivo, OperadorId = operadorId }, tx);

        await conn.ExecuteAsync("""
            insert into plusbt.template_versoes
                (template_id, versao, nome_arquivo, conteudo, hash_sha256, tamanho, origem, enviado_por)
            values (@TemplateId, @Versao, @NomeArquivo, @Conteudo, @Hash, @Tamanho, @Origem, @OperadorId);

            update plusbt.templates
            set    versao_atual = @Versao, hash_atual = @Hash, nome_arquivo = @NomeArquivo,
                   atualizado_em = now(), atualizado_por = @OperadorId
            where  id = @TemplateId;

            delete from plusbt.template_versoes
            where  template_id = @TemplateId and versao < @Versao - @Backups;
            """, new
        {
            TemplateId = templateId,
            Versao = novaVersao,
            NomeArquivo = nomeArquivo,
            Conteudo = conteudo,
            Hash = hash,
            Tamanho = conteudo.Length,
            Origem = origem,
            OperadorId = operadorId,
            Backups = VersoesDeBackup
        }, tx);

        await tx.CommitAsync(ct);
        return new EnvioTemplate(codigo, existe ? ResultadoEnvio.NovaVersao : ResultadoEnvio.Criado, novaVersao);
    }
}
