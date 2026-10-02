using Busca_BT.Models;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using System.IO;

namespace Busca_BT.Data
{
    // ────────────────────────────────────────────────────────────────────────────
    // Interface
    // ────────────────────────────────────────────────────────────────────────────

    public interface ILabelRepository
    {
        Task<IReadOnlyList<LabelRecord>> GetAllAsync();
        Task<IEnumerable<LabelRecord>> GetAllTemplatesMasterAsync(); // Busca apenas o Acervo
        Task<bool> AssociateLabelFileAsync(int id, string filePath);
        Task<bool> MarcarComoAbertaAsync(int id, CancellationToken ct = default);
        Task<IEnumerable<ImportBatchRecord>> GetBatchesAsync(CancellationToken ct = default);
        Task DeleteBatchAsync(int batchId, CancellationToken ct = default);

        /// <summary>
        /// Registra a importação e substitui a fila pelos itens dela, numa transação
        /// única: se a conexão cair no meio, nada muda. Retorna o id da importação.
        /// </summary>
        Task<int> ReplaceQueueAsync(string fileName, int total, int skipped, IReadOnlyList<LabelRecord> records);

        Task<int> ClearQueueAsync(); // Apaga a fila atual (o histórico de importações é mantido)
        Task<int> UpsertTemplatesAsync(IEnumerable<(string FileName, string FilePath)> templates);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Implementação com Dapper + Npgsql (Supabase / Postgres, schema plusbt)
    // ────────────────────────────────────────────────────────────────────────────

    public sealed partial class LabelRepository(
        IDbConnectionFactory factory,
        ILogger<LabelRepository> logger) : ILabelRepository
    {
        // ── READ ─────────────────────────────────────────────────────────────

        // O vínculo com o template (plusbt.templates) não é feito aqui com JOIN exato: é feito
        // em LabelDiagnostics, que tolera espaços, maiúsculas, zeros à esquerda e sufixos.
        private const string BaseSelectSql = """
            select id               as "Id",
                   item             as "Item",
                   importacao_id    as "BatchId",
                   invoice          as "Invoice",
                   codigo           as "Codigo",
                   descricao        as "DescricaoAnvisa",
                   qtd              as "QtdInvoice",
                   lote             as "Lote",
                   validade         as "Validade",
                   validade_texto   as "ValidadeTexto",
                   registro_anvisa  as "RegistroAnvisa",
                   lpn              as "Lpn",
                   local            as "Local",
                   avisos           as "Avisos",
                   importado_em     as "ImportedAt",
                   atualizado_em    as "UpdatedAt",
                   aberta_em        as "AbertaEm"
            from   plusbt.itens
            """;

        public async Task<IReadOnlyList<LabelRecord>> GetAllAsync()
        {
            try
            {
                await using var conn = await factory.OpenAsync();
                // Ordem de inserção = ordem da planilha (o Item é numerado por invoice).
                var rows = await conn.QueryAsync<LabelRecord>($"{BaseSelectSql} order by id;");
                return rows.AsList();
            }
            catch (Exception ex)
            {
                LogGetAllError(logger, ex);
                throw;
            }
        }

        // Traz apenas os templates cadastrados no acervo (para a tela Templates e o vínculo da fila)
        public async Task<IEnumerable<LabelRecord>> GetAllTemplatesMasterAsync()
        {
            const string sql = """
                select id             as "Id",
                       codigo         as "Codigo",
                       ''             as "DescricaoAnvisa",
                       arquivo        as "LabelFilePath",
                       atualizado_em  as "UpdatedAt"
                from   plusbt.templates
                order  by codigo;
                """;

            try
            {
                await using var conn = await factory.OpenAsync();
                return await conn.QueryAsync<LabelRecord>(sql);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao buscar Master Templates");
                throw;
            }
        }

        public async Task<IEnumerable<ImportBatchRecord>> GetBatchesAsync(CancellationToken ct = default)
        {
            const string sql = """
                select id            as "Id",
                       arquivo       as "FileName",
                       importado_em  as "ImportedAt",
                       total         as "TotalRows",
                       importados    as "ImportedRows",
                       ignorados     as "SkippedRows"
                from   plusbt.importacoes
                order  by importado_em desc;
                """;

            try
            {
                await using var conn = await factory.OpenAsync(ct);
                return await conn.QueryAsync<ImportBatchRecord>(sql);
            }
            catch (Exception ex)
            {
                LogGetBatchesError(logger, ex);
                throw;
            }
        }

        // ── WRITE ────────────────────────────────────────────────────────────

        public async Task<bool> AssociateLabelFileAsync(int id, string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Arquivo não encontrado: {filePath}", filePath);

            const string sql = """
                update plusbt.templates
                set    arquivo = @FilePath,
                       atualizado_em = now()
                where  id = @Id;
                """;

            try
            {
                await using var conn = await factory.OpenAsync();
                var rows = await conn.ExecuteAsync(sql, new { FilePath = filePath, Id = id });

                if (rows > 0)
                    LogAssociateFileSuccess(logger, filePath, id);
                else
                    LogAssociateFileNotFound(logger, id);

                return rows > 0;
            }
            catch (Exception ex)
            {
                LogAssociateFileError(logger, id, ex);
                throw;
            }
        }

        public async Task<bool> MarcarComoAbertaAsync(int id, CancellationToken ct = default)
        {
            const string sql = """
                update plusbt.itens
                set    aberta_em = now(),
                       atualizado_em = now()
                where  id = @Id and aberta_em is null;
                """;

            try
            {
                await using var conn = await factory.OpenAsync(ct);
                var rows = await conn.ExecuteAsync(sql, new { Id = id });
                return rows > 0;
            }
            catch (Exception ex)
            {
                LogMarcarAbertaError(logger, id, ex);
                throw;
            }
        }

        public async Task DeleteBatchAsync(int batchId, CancellationToken ct = default)
        {
            // Os itens da importação saem junto (on delete cascade).
            const string sql = "delete from plusbt.importacoes where id = @Id;";

            try
            {
                await using var conn = await factory.OpenAsync(ct);
                await conn.ExecuteAsync(sql, new { Id = batchId });
            }
            catch (Exception ex)
            {
                LogDeleteBatchError(logger, batchId, ex);
                throw;
            }
        }

        public async Task<int> ClearQueueAsync()
        {
            try
            {
                await using var conn = await factory.OpenAsync();
                var rows = await conn.ExecuteAsync("delete from plusbt.itens;");
                LogQueueCleared(logger, rows);
                return rows;
            }
            catch (Exception ex)
            {
                LogClearQueueError(logger, ex);
                throw;
            }
        }

        public async Task<int> ReplaceQueueAsync(
            string fileName, int total, int skipped, IReadOnlyList<LabelRecord> records)
        {
            const string sqlBatch = """
                insert into plusbt.importacoes (arquivo, total, importados, ignorados)
                values (@Arquivo, @Total, @Importados, @Ignorados)
                returning id;
                """;

            // COPY binário: todas as linhas numa ida só ao servidor (rápido mesmo com internet lenta).
            const string sqlCopy = """
                copy plusbt.itens
                    (importacao_id, item, invoice, codigo, descricao, qtd, lote, validade,
                     validade_texto, registro_anvisa, lpn, local, avisos, importado_em)
                from stdin (format binary)
                """;

            var batchId = 0;
            try
            {
                await using var conn = await factory.OpenAsync();
                await using var tx = await conn.BeginTransactionAsync();

                batchId = await conn.QuerySingleAsync<int>(sqlBatch, new
                {
                    Arquivo = Path.GetFileName(fileName),
                    Total = total,
                    Importados = records.Count,
                    Ignorados = skipped
                }, tx);

                // Apaga a fila antiga (o histórico de importações é mantido).
                await conn.ExecuteAsync("delete from plusbt.itens;", transaction: tx);

                await using (var writer = await conn.BeginBinaryImportAsync(sqlCopy))
                {
                    foreach (var r in records)
                    {
                        await writer.StartRowAsync();
                        await writer.WriteAsync(batchId, NpgsqlDbType.Integer);
                        await writer.WriteAsync(r.Item, NpgsqlDbType.Integer);
                        await writer.WriteAsync(r.Invoice, NpgsqlDbType.Text);
                        await writer.WriteAsync(r.Codigo, NpgsqlDbType.Text);
                        await writer.WriteAsync(r.DescricaoAnvisa, NpgsqlDbType.Text);
                        await writer.WriteAsync(r.QtdInvoice, NpgsqlDbType.Integer);
                        await writer.WriteAsync(r.Lote, NpgsqlDbType.Text);
                        if (r.Validade is DateTime validade)
                            await writer.WriteAsync(DateOnly.FromDateTime(validade), NpgsqlDbType.Date);
                        else
                            await writer.WriteNullAsync();
                        await WriteNullableTextAsync(writer, r.ValidadeTexto);
                        await writer.WriteAsync(r.RegistroAnvisa, NpgsqlDbType.Text);
                        await writer.WriteAsync(r.Lpn, NpgsqlDbType.Text);
                        await writer.WriteAsync(r.Local, NpgsqlDbType.Text);
                        await WriteNullableTextAsync(writer, r.Avisos);
                        await writer.WriteAsync(DateTime.SpecifyKind(r.ImportedAt, DateTimeKind.Utc), NpgsqlDbType.TimestampTz);
                    }

                    await writer.CompleteAsync();
                }

                await tx.CommitAsync();
                LogInsertBatchDone(logger, records.Count, batchId);
                return batchId;
            }
            catch (Exception ex)
            {
                LogInsertBatchError(logger, batchId, ex);
                throw;
            }
        }

        private static async Task WriteNullableTextAsync(NpgsqlBinaryImporter writer, string? value)
        {
            if (value is null)
                await writer.WriteNullAsync();
            else
                await writer.WriteAsync(value, NpgsqlDbType.Text);
        }

        public async Task<int> UpsertTemplatesAsync(IEnumerable<(string FileName, string FilePath)> templates)
        {
            const string sqlUpsert = """
                insert into plusbt.templates (codigo, arquivo, atualizado_em)
                values (@Codigo, @FilePath, now())
                on conflict (codigo) do update
                    set arquivo = excluded.arquivo,
                        atualizado_em = now();
                """;

            try
            {
                await using var conn = await factory.OpenAsync();
                await using var tx = await conn.BeginTransactionAsync();

                var lista = templates.Select(t => new { Codigo = t.FileName, t.FilePath }).ToList();
                var count = await conn.ExecuteAsync(sqlUpsert, lista, tx);

                await tx.CommitAsync();
                return count;
            }
            catch (Exception ex)
            {
                LogInsertBatchError(logger, 0, ex);
                throw;
            }
        }

        // ── LoggerMessage source generators (CA1848) ─────────────────────────

        [LoggerMessage(Level = LogLevel.Error, Message = "Erro ao buscar etiquetas")]
        private static partial void LogGetAllError(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Information, Message = "{Count} etiquetas inseridas (BatchId={BatchId})")]
        private static partial void LogInsertBatchDone(ILogger logger, int count, int batchId);

        [LoggerMessage(Level = LogLevel.Error, Message = "Erro ao inserir lote de etiquetas (BatchId={BatchId})")]
        private static partial void LogInsertBatchError(ILogger logger, int batchId, Exception ex);

        [LoggerMessage(Level = LogLevel.Information, Message = "Arquivo '{FilePath}' associado à etiqueta Id={Id}")]
        private static partial void LogAssociateFileSuccess(ILogger logger, string filePath, int id);

        [LoggerMessage(Level = LogLevel.Warning, Message = "AssociateLabelFile: Id={Id} não encontrado")]
        private static partial void LogAssociateFileNotFound(ILogger logger, int id);

        [LoggerMessage(Level = LogLevel.Error, Message = "Erro ao associar arquivo à etiqueta Id={Id}")]
        private static partial void LogAssociateFileError(ILogger logger, int id, Exception ex);

        [LoggerMessage(Level = LogLevel.Error, Message = "Erro ao marcar etiqueta Id={Id} como aberta")]
        private static partial void LogMarcarAbertaError(ILogger logger, int id, Exception ex);

        [LoggerMessage(Level = LogLevel.Error, Message = "Erro ao deletar etiquetas do lote {BatchId}")]
        private static partial void LogDeleteBatchError(ILogger logger, int batchId, Exception ex);

        [LoggerMessage(Level = LogLevel.Information, Message = "Fila limpa: {Count} etiqueta(s) removida(s)")]
        private static partial void LogQueueCleared(ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Error, Message = "Erro ao limpar a fila de etiquetas")]
        private static partial void LogClearQueueError(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Error, Message = "Erro ao buscar etiquetas do lote")]
        private static partial void LogGetBatchesError(ILogger logger, Exception ex);
    }
}
