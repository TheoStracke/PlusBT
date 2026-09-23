using Busca_BT.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace Busca_BT.Data
{
    // ────────────────────────────────────────────────────────────────────────────
    // Interface
    // ────────────────────────────────────────────────────────────────────────────

    public interface ILabelRepository
    {
        Task<IReadOnlyList<LabelRecord>> GetAllAsync(LabelFilter? filter = null);
        Task<IEnumerable<LabelRecord>> GetAllTemplatesMasterAsync(); // NOVO: Busca apenas o Acervo
        Task<LabelRecord?> GetByIdAsync(int id);
        Task<int> InsertBatchAsync(IEnumerable<LabelRecord> records, int batchId);
        Task<bool> AssociateLabelFileAsync(int id, string filePath);
        Task<bool> UpdateStatusAsync(int id, LabelStatus status);
        Task<bool> MarcarComoAbertaAsync(int id, CancellationToken ct = default);
        Task<int> DeleteByBatchAsync(int batchId);
        Task<int> CreateImportBatchAsync(string fileName, int total, int imported, int skipped);
        Task AtualizarHashPorCodigoAsync(string codigo, string filePath, string hash, CancellationToken ct = default);
        Task<IEnumerable<ImportBatchRecord>> GetBatchesAsync(CancellationToken ct = default);
        Task<IEnumerable<LabelRecord>> GetByBatchIdAsync(int batchId, CancellationToken ct = default);
        Task DeleteBatchAsync(int batchId, CancellationToken ct = default);
        Task<int> ClearAllLabelsAsync();
        Task<int> ReplaceAllAsync(IEnumerable<LabelRecord> records, int batchId);
        Task<DateTime?> GetLatestUpdateTimestampAsync();
        Task<int> UpsertTemplatesAsync(IEnumerable<(string FileName, string FilePath)> templates);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Implementação com Dapper
    // ────────────────────────────────────────────────────────────────────────────

    public sealed partial class LabelRepository(
        IDbConnectionFactory factory,
        ILogger<LabelRepository> logger) : ILabelRepository
    {
        // ── READ ─────────────────────────────────────────────────────────────

        public async Task<IReadOnlyList<LabelRecord>> GetAllAsync(LabelFilter? filter = null)
        {
            try
            {
                await using var conn = (SqlConnection)await factory.OpenAsync();

                var sql = new StringBuilder(BaseSelectSql);
                var param = new DynamicParameters();

                if (filter is not null)
                {
                    var clauses = new List<string>();

                    if (!string.IsNullOrWhiteSpace(filter.Lote))
                    {
                        clauses.Add("L.Lote LIKE @Lote");
                        param.Add("Lote", $"%{filter.Lote}%");
                    }

                    if (!string.IsNullOrWhiteSpace(filter.Lpn))
                    {
                        clauses.Add("L.Lpn LIKE @Lpn");
                        param.Add("Lpn", $"%{filter.Lpn}%");
                    }

                    if (!string.IsNullOrWhiteSpace(filter.Invoice))
                    {
                        clauses.Add("L.Invoice LIKE @Invoice");
                        param.Add("Invoice", $"%{filter.Invoice}%");
                    }

                    if (filter.Status.HasValue)
                    {
                        clauses.Add("L.Status = @Status");
                        param.Add("Status", (int)filter.Status.Value);
                    }

                    if (clauses.Count > 0)
                        sql.Append(" WHERE ").Append(string.Join(" AND ", clauses));
                }

                sql.Append(" ORDER BY L.Item ASC;");

                var rows = await conn.QueryAsync<LabelRecord>(sql.ToString(), param);
                return rows.AsList();
            }
            catch (Exception ex)
            {
                LogGetAllError(logger, ex);
                throw;
            }
        }

        // NOVO MÉTOD: Traz apenas os templates cadastrados no acervo intocável (Para a tela de Gerenciamento)
        public async Task<IEnumerable<LabelRecord>> GetAllTemplatesMasterAsync()
        {
            var sql = @"
                SELECT 
                    Id, 
                    Codigo, 
                    '' AS DescricaoAnvisa, 
                    LabelFilePath, 
                    UpdatedAt 
                FROM dbo.Templates 
                ORDER BY Codigo ASC";

            try
            {
                await using var conn = (SqlConnection)await factory.OpenAsync();
                return await conn.QueryAsync<LabelRecord>(sql);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao buscar Master Templates");
                throw;
            }
        }

        public async Task<LabelRecord?> GetByIdAsync(int id)
        {
            try
            {
                await using var conn = (SqlConnection)await factory.OpenAsync();
                return await conn.QueryFirstOrDefaultAsync<LabelRecord>(
                    $"{BaseSelectSql} WHERE L.Id = @Id;",
                    new { Id = id });
            }
            catch (Exception ex)
            {
                LogGetByIdError(logger, id, ex);
                throw;
            }
        }

        // ── WRITE ────────────────────────────────────────────────────────────

        public async Task<int> InsertBatchAsync(IEnumerable<LabelRecord> records, int batchId)
        {
            const string insertSql = """
                INSERT INTO dbo.Labels
                    (Item, Invoice, Codigo, DescricaoAnvisa, QtdInvoice,
                     Lote, Validade, RegistroAnvisa, Lpn, LabelFilePath, Status, ImportedAt, BatchId)
                VALUES
                    (@Item, @Invoice, @Codigo, @DescricaoAnvisa, @QtdInvoice,
                     @Lote, @Validade, @RegistroAnvisa, @Lpn, @LabelFilePath, @Status, @ImportedAt, @BatchId);
                """;

            try
            {
                await using var conn = (SqlConnection)await factory.OpenAsync();
                await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();

                var inserted = 0;
                foreach (var r in records)
                {
                    await conn.ExecuteAsync(insertSql, new
                    {
                        r.Item,
                        r.Invoice,
                        r.Codigo,
                        r.DescricaoAnvisa,
                        r.QtdInvoice,
                        r.Lote,
                        r.Validade,
                        r.RegistroAnvisa,
                        r.Lpn,
                        r.LabelFilePath, // Será null no ExcelImportService agora, o JOIN cuidará do resto
                        Status = (int)r.Status,
                        r.ImportedAt,
                        BatchId = batchId
                    }, tx);
                    inserted++;
                }

                tx.Commit();
                LogInsertBatchDone(logger, inserted, batchId);
                return inserted;
            }
            catch (Exception ex)
            {
                LogInsertBatchError(logger, batchId, ex);
                throw;
            }
        }

        public async Task<bool> AssociateLabelFileAsync(int id, string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Arquivo não encontrado: {filePath}", filePath);

            // ATENÇÃO: Agora ele atualiza o ACERVO (dbo.Templates), mantendo o histórico protegido!
            const string sql = """
                UPDATE dbo.Templates
                SET    LabelFilePath = @FilePath,
                       UpdatedAt     = SYSUTCDATETIME()
                WHERE  Id = @Id;
                """;

            try
            {
                await using var conn = (SqlConnection)await factory.OpenAsync();
                var rows = await conn.ExecuteAsync(sql, new
                {
                    FilePath = filePath,
                    Id = id
                });

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

        public async Task<bool> UpdateStatusAsync(int id, LabelStatus status)
        {
            const string sql = """
                UPDATE dbo.Labels
                SET    Status    = @Status,
                       UpdatedAt = SYSUTCDATETIME()
                WHERE  Id = @Id;
                """;

            try
            {
                await using var conn = (SqlConnection)await factory.OpenAsync();
                var rows = await conn.ExecuteAsync(sql, new { Status = (int)status, Id = id });
                return rows > 0;
            }
            catch (Exception ex)
            {
                LogUpdateStatusError(logger, id, ex);
                throw;
            }
        }

        public async Task<bool> MarcarComoAbertaAsync(int id, CancellationToken ct = default)
        {
            const string sql = """
                UPDATE dbo.Labels
                SET    AbertaEm = SYSUTCDATETIME()
                WHERE  Id = @Id AND AbertaEm IS NULL;
                """;

            try
            {
                await using var conn = (SqlConnection)await factory.OpenAsync(ct);
                var rows = await conn.ExecuteAsync(sql, new { Id = id });
                return rows > 0;
            }
            catch (Exception ex)
            {
                LogMarcarAbertaError(logger, id, ex);
                throw;
            }
        }

        public async Task<int> DeleteByBatchAsync(int batchId)
        {
            try
            {
                await using var conn = (SqlConnection)await factory.OpenAsync();
                return await conn.ExecuteAsync(
                    "DELETE FROM dbo.Labels WHERE BatchId = @BatchId;",
                    new { BatchId = batchId });
            }
            catch (Exception ex)
            {
                LogDeleteBatchError(logger, batchId, ex);
                throw;
            }
        }

        public async Task<int> CreateImportBatchAsync(
            string fileName, int total, int imported, int skipped)
        {
            const string sql = """
                INSERT INTO dbo.ImportBatches (FileName, TotalRows, ImportedRows, SkippedRows)
                OUTPUT INSERTED.Id
                VALUES (@FileName, @TotalRows, @ImportedRows, @SkippedRows);
                """;

            try
            {
                await using var conn = (SqlConnection)await factory.OpenAsync();
                return await conn.QuerySingleAsync<int>(sql, new
                {
                    FileName = Path.GetFileName(fileName),
                    TotalRows = total,
                    ImportedRows = imported,
                    SkippedRows = skipped
                });
            }
            catch (Exception ex)
            {
                LogCreateBatchError(logger, fileName, ex);
                throw;
            }
        }

        public async Task AtualizarHashPorCodigoAsync(string codigo, string filePath, string hash, CancellationToken ct = default)
        {
            const string sql = """
                UPDATE dbo.Templates
                SET    LabelFilePath = @FilePath,
                       UpdatedAt     = SYSUTCDATETIME()
                WHERE  Codigo = @Codigo
                """;

            try
            {
                await using var conn = (SqlConnection)await factory.OpenAsync(ct);
                await conn.ExecuteAsync(sql, new { FilePath = filePath, Codigo = codigo });
            }
            catch (Exception ex)
            {
                LogUpdateHashError(logger, codigo, ex);
                throw;
            }
        }

        public async Task<IEnumerable<ImportBatchRecord>> GetBatchesAsync(CancellationToken ct = default)
        {
            const string sql = """
                SELECT Id, FileName, ImportedAt, TotalRows, ImportedRows, SkippedRows
                FROM   dbo.ImportBatches
                ORDER  BY ImportedAt DESC
                """;

            try
            {
                await using var conn = (SqlConnection)await factory.OpenAsync(ct);
                return await conn.QueryAsync<ImportBatchRecord>(sql);
            }
            catch (Exception ex)
            {
                LogGetBatchesError(logger, ex);
                throw;
            }
        }

        public async Task<IEnumerable<LabelRecord>> GetByBatchIdAsync(int batchId, CancellationToken ct = default)
        {
            try
            {
                await using var conn = (SqlConnection)await factory.OpenAsync(ct);
                return await conn.QueryAsync<LabelRecord>(
                    $"{BaseSelectSql} WHERE L.BatchId = @BatchId ORDER BY L.Item ASC;",
                    new { BatchId = batchId });
            }
            catch (Exception ex)
            {
                LogGetByBatchIdError(logger, batchId, ex);
                throw;
            }
        }

        public async Task DeleteBatchAsync(int batchId, CancellationToken ct = default)
        {
            const string sql = """
                DELETE FROM dbo.Labels        WHERE BatchId = @Id;
                DELETE FROM dbo.ImportBatches WHERE Id      = @Id;
                """;

            try
            {
                await using var conn = (SqlConnection)await factory.OpenAsync(ct);
                await conn.ExecuteAsync(sql, new { Id = batchId });
            }
            catch (Exception ex)
            {
                LogDeleteBatchError(logger, batchId, ex);
                throw;
            }
        }

        public async Task<int> ClearAllLabelsAsync()
        {
            try
            {
                await using var conn = (SqlConnection)await factory.OpenAsync();
                return await conn.ExecuteAsync("DELETE FROM dbo.Labels;");
            }
            catch (Exception ex)
            {
                LogDeleteBatchError(logger, 0, ex);
                throw;
            }
        }

        public async Task<int> ReplaceAllAsync(IEnumerable<LabelRecord> records, int batchId)
        {
            const string sqlDelete = "DELETE FROM dbo.Labels;";
            const string sqlInsert = """
                INSERT INTO dbo.Labels
                    (Item, Invoice, Codigo, DescricaoAnvisa, QtdInvoice,
                     Lote, Validade, RegistroAnvisa, Lpn, LabelFilePath, Status, ImportedAt, BatchId)
                VALUES
                    (@Item, @Invoice, @Codigo, @DescricaoAnvisa, @QtdInvoice,
                     @Lote, @Validade, @RegistroAnvisa, @Lpn, @LabelFilePath, @Status, @ImportedAt, @BatchId);
                """;

            try
            {
                await using var conn = (SqlConnection)await factory.OpenAsync();
                await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();

                // Apaga a Fila do Dia antiga!
                await conn.ExecuteAsync(sqlDelete, transaction: tx);

                var inserted = 0;
                foreach (var r in records)
                {
                    await conn.ExecuteAsync(sqlInsert, new
                    {
                        r.Item,
                        r.Invoice,
                        r.Codigo,
                        r.DescricaoAnvisa,
                        r.QtdInvoice,
                        r.Lote,
                        r.Validade,
                        r.RegistroAnvisa,
                        r.Lpn,
                        r.LabelFilePath, // Estará NULL (vem do ExcelImportService)
                        Status = (int)r.Status,
                        r.ImportedAt,
                        BatchId = batchId
                    }, tx);
                    inserted++;
                }

                tx.Commit();
                LogInsertBatchDone(logger, inserted, batchId);
                return inserted;
            }
            catch (Exception ex)
            {
                LogInsertBatchError(logger, batchId, ex);
                throw;
            }
        }

        public async Task<DateTime?> GetLatestUpdateTimestampAsync()
        {
            const string sql = "SELECT MAX(UpdatedAt) FROM dbo.Templates;";

            try
            {
                await using var conn = (SqlConnection)await factory.OpenAsync();
                var result = await conn.ExecuteScalarAsync<DateTime?>(sql);
                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao obter timestamp da última atualização");
                throw;
            }
        }

        public async Task<int> UpsertTemplatesAsync(IEnumerable<(string FileName, string FilePath)> templates)
        {
            // Salva apenas no Acervo (Tabela Templates)
            const string sqlUpsert = """
                IF EXISTS (SELECT 1 FROM dbo.Templates WHERE Codigo = @Codigo)
                    UPDATE dbo.Templates SET LabelFilePath = @FilePath, UpdatedAt = SYSUTCDATETIME() WHERE Codigo = @Codigo;
                ELSE
                    INSERT INTO dbo.Templates (Codigo, LabelFilePath, UpdatedAt) VALUES (@Codigo, @FilePath, SYSUTCDATETIME());
                """;

            try
            {
                await using var conn = (SqlConnection)await factory.OpenAsync();
                await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();

                var count = 0;
                foreach (var (fileName, filePath) in templates)
                {
                    var rows = await conn.ExecuteAsync(sqlUpsert, new { Codigo = fileName, FilePath = filePath }, tx);
                    if (rows > 0) count++;
                }

                tx.Commit();
                return count;
            }
            catch (Exception ex)
            {
                LogInsertBatchError(logger, 0, ex);
                throw;
            }
        }

        // ── SQL base ─────────────────────────────────────────────────────────

        // A MÁGICA ACONTECE AQUI: Cruzamento da fila (Labels) com os arquivos do acervo (Templates)
        private const string BaseSelectSql = """
            SELECT L.Id, L.Item, L.Invoice, L.Codigo, L.DescricaoAnvisa, L.QtdInvoice,
                   L.Lote, L.Validade, L.RegistroAnvisa, L.Lpn,
                   T.LabelFilePath, L.Status, L.ImportedAt, L.UpdatedAt, L.AbertaEm
            FROM   dbo.Labels L
            LEFT JOIN dbo.Templates T ON L.Codigo = T.Codigo
            """;

        // ── LoggerMessage source generators (CA1848) ─────────────────────────

        [LoggerMessage(Level = LogLevel.Error, Message = "Erro ao buscar etiquetas")]
        private static partial void LogGetAllError(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Error, Message = "Erro ao buscar etiqueta Id={Id}")]
        private static partial void LogGetByIdError(ILogger logger, int id, Exception ex);

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

        [LoggerMessage(Level = LogLevel.Error, Message = "Erro ao atualizar status da etiqueta Id={Id}")]
        private static partial void LogUpdateStatusError(ILogger logger, int id, Exception ex);

        [LoggerMessage(Level = LogLevel.Error, Message = "Erro ao marcar etiqueta Id={Id} como aberta")]
        private static partial void LogMarcarAbertaError(ILogger logger, int id, Exception ex);

        [LoggerMessage(Level = LogLevel.Error, Message = "Erro ao deletar etiquetas do lote {BatchId}")]
        private static partial void LogDeleteBatchError(ILogger logger, int batchId, Exception ex);

        [LoggerMessage(Level = LogLevel.Error, Message = "Erro ao criar registro de lote para '{FileName}'")]
        private static partial void LogCreateBatchError(ILogger logger, string fileName, Exception ex);

        [LoggerMessage(Level = LogLevel.Error, Message = "Erro ao atualizar hash da etiqueta Codigo={Codigo}")]
        private static partial void LogUpdateHashError(ILogger logger, string codigo, Exception ex);

        [LoggerMessage(Level = LogLevel.Error, Message = "Erro ao buscar etiquetas do lote")]
        private static partial void LogGetBatchesError(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Error, Message = "Erro ao buscar etiquetas por BatchId={BatchId}")]
        private static partial void LogGetByBatchIdError(ILogger logger, int batchId, Exception ex);
    }
}