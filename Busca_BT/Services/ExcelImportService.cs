using Busca_BT.Data;
using Busca_BT.Models;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace Busca_BT.Services
{
    public interface IExcelImportService
    {
        Task<ImportResult> ImportAsync(string filePath, CancellationToken cancellationToken = default);
    }

    public sealed partial class ExcelImportService(
        ILabelRepository repository,
        ILogger<ExcelImportService> logger,
        EtiquetasOptions options) : IExcelImportService
    {
        private const string SheetName = "Planilha1";

        // Índices de coluna (base 1) — altere aqui se a planilha mudar
        private const int ColItem = 1;
        private const int ColInvoice = 2;
        private const int ColCodigo = 3;
        private const int ColDescricao = 4;
        private const int ColQtdInvoice = 5;
        private const int ColLote = 6;
        private const int ColValidade = 7;
        private const int ColRegistroAnvisa = 8;
        private const int ColLpn = 9;
        private const int ColTemplateFilePath = 10;

        public async Task<ImportResult> ImportAsync(
            string filePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return ImportResult.Fail("Caminho do arquivo não pode ser vazio.");

            if (!File.Exists(filePath))
                return ImportResult.Fail($"Arquivo não encontrado: {filePath}");

            if (!filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                return ImportResult.Fail("Apenas arquivos .xlsx são suportados.");

            LogImportStarted(logger, filePath);

            try
            {
                using var workbook = new XLWorkbook(filePath);

                if (!workbook.TryGetWorksheet(SheetName, out var sheet))
                {
                    var available = string.Join(", ", workbook.Worksheets.Select(w => w.Name));
                    return ImportResult.Fail(
                        $"Aba '{SheetName}' não encontrada. Abas disponíveis: {available}");
                }

                var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;

                if (lastRow < 2)
                    return ImportResult.Fail("Planilha sem dados (apenas cabeçalho ou vazia).");

                var records = new List<LabelRecord>();
                var rowErrors = new List<string>();
                var skipped = 0;

                // Variável que vai forçar a sequência correta, não importa o que esteja no Excel
                var currentItemSequence = 1;

                for (int row = 2; row <= lastRow; row++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Passamos a nossa sequência forçada para o TryParseRow
                    if (TryParseRow(sheet, row, currentItemSequence, out var record, out var error))
                    {
                        records.Add(record!);
                        currentItemSequence++; // Só incrementa se a linha for válida e importada com sucesso
                    }
                    else
                    {
                        rowErrors.Add($"Linha {row}: {error}");
                        skipped++;
                        LogRowSkipped(logger, row, error!);
                    }
                }

                if (records.Count == 0)
                    return ImportResult.Fail(
                        $"Nenhuma linha válida. Erros: {string.Join("; ", rowErrors)}");

                int batchId = await repository.CreateImportBatchAsync(
                    filePath, total: lastRow - 1, imported: records.Count, skipped: skipped);

                int insertedCount = await repository.ReplaceAllAsync(records, batchId);

                LogImportFinished(logger, lastRow - 1, insertedCount, skipped);

                return ImportResult.Ok(lastRow - 1, insertedCount, skipped, rowErrors, 0);
            }
            catch (OperationCanceledException)
            {
                LogImportCancelled(logger, filePath);
                throw;
            }
            catch (Exception ex)
            {
                LogImportError(logger, filePath, ex);
                return ImportResult.Fail($"Erro ao processar o arquivo: {ex.Message}");
            }
        }

        // ── Parse de linha individual ─────────────────────────────────────────

        private static bool TryParseRow(
            IXLWorksheet sheet,
            int rowNum,
            int expectedItemSequence, // Recebe a sequência correta forçada pelo loop acima
            out LabelRecord? record,
            out string? error)
        {
            record = null;
            error = null;

            try
            {
                // REMOVEMOS a validação restrita da ColItem. 
                // Ignoramos a coluna 1 do Excel para não travar a importação.
                int item = expectedItemSequence;

                var invoice = GetString(sheet, rowNum, ColInvoice);
                if (string.IsNullOrWhiteSpace(invoice))
                { error = "Coluna 'Invoice' está vazia."; return false; }

                var codigo = GetString(sheet, rowNum, ColCodigo);
                if (string.IsNullOrWhiteSpace(codigo))
                { error = "Coluna 'Código' está vazia."; return false; }

                var descricao = GetString(sheet, rowNum, ColDescricao);

                if (!TryGetInt(sheet, rowNum, ColQtdInvoice, out int qtd))
                {
                    error = $"Coluna 'Qtd Invoice' inválida ('{GetRaw(sheet, rowNum, ColQtdInvoice)}').";
                    return false;
                }

                var lote = GetString(sheet, rowNum, ColLote);
                if (string.IsNullOrWhiteSpace(lote))
                { error = "Coluna 'Lote' está vazia."; return false; }

                if (!TryGetDate(sheet, rowNum, ColValidade, out DateTime validade))
                {
                    error = $"Coluna 'Validade' inválida ('{GetRaw(sheet, rowNum, ColValidade)}').";
                    return false;
                }

                var registroAnvisa = GetString(sheet, rowNum, ColRegistroAnvisa);

                var lpn = GetString(sheet, rowNum, ColLpn);
                if (string.IsNullOrWhiteSpace(lpn))
                { error = "Coluna 'LPN' está vazia."; return false; }

                record = new LabelRecord
                {
                    Item = item, // Recebe a nossa sequência perfeita calculada matematicamente
                    Invoice = invoice,
                    Codigo = codigo,
                    DescricaoAnvisa = descricao,
                    QtdInvoice = qtd,
                    Lote = lote,
                    Validade = validade,
                    RegistroAnvisa = registroAnvisa,
                    Lpn = lpn,
                    LabelFilePath = null,
                    Status = LabelStatus.Pendente,
                    ImportedAt = DateTime.UtcNow
                };

                return true;
            }
            catch (Exception ex)
            {
                error = $"Exceção inesperada: {ex.Message}";
                return false;
            }
        }

        // ── Helpers de célula ─────────────────────────────────────────────────

        private static string GetString(IXLWorksheet sheet, int row, int col)
            => sheet.Cell(row, col).GetValue<string>()?.Trim() ?? string.Empty;

        private static string GetRaw(IXLWorksheet sheet, int row, int col)
            => sheet.Cell(row, col).Value.ToString() ?? string.Empty;

        private static bool TryGetInt(IXLWorksheet sheet, int row, int col, out int value)
        {
            var cell = sheet.Cell(row, col);
            if (cell.Value.IsNumber) { value = (int)cell.GetValue<double>(); return true; }
            return int.TryParse(cell.GetValue<string>()?.Trim(), out value);
        }

        private static bool TryGetDate(IXLWorksheet sheet, int row, int col, out DateTime value)
        {
            var cell = sheet.Cell(row, col);

            if (cell.Value.IsDateTime) { value = cell.GetValue<DateTime>(); return true; }

            if (cell.Value.IsNumber)
            {
                try { value = DateTime.FromOADate(cell.GetValue<double>()); return true; }
                catch { /* continua para parse de string */ }
            }

            var raw = cell.GetValue<string>()?.Trim();
            if (!string.IsNullOrEmpty(raw))
            {
                string[] formats = ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "yyyy-MM-dd", "MM/dd/yyyy"];
                if (DateTime.TryParseExact(raw, formats,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out value))
                    return true;
            }

            value = default;
            return false;
        }

        // ── LoggerMessage source generators (CA1848) ─────────────────────────

        [LoggerMessage(Level = LogLevel.Information, Message = "Iniciando importação de: {FilePath}")]
        private static partial void LogImportStarted(ILogger logger, string filePath);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Linha ignorada {Row}: {Error}")]
        private static partial void LogRowSkipped(ILogger logger, int row, string error);

        [LoggerMessage(Level = LogLevel.Information,
            Message = "Importação concluída. Total={Total} | Importadas={Imported} | Ignoradas={Skipped}")]
        private static partial void LogImportFinished(ILogger logger, int total, int imported, int skipped);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Importação cancelada pelo usuário: {FilePath}")]
        private static partial void LogImportCancelled(ILogger logger, string filePath);

        [LoggerMessage(Level = LogLevel.Error, Message = "Erro inesperado ao importar planilha: {FilePath}")]
        private static partial void LogImportError(ILogger logger, string filePath, Exception ex);
    }
}