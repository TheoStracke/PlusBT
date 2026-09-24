using Busca_BT.Data;
using Busca_BT.Models;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
        ILogger<ExcelImportService> logger) : IExcelImportService
    {
        private const int HeaderRow = 1;

        // Colunas obrigatórias: nome lógico do campo -> variações de cabeçalho aceitas
        // (cobre tanto o layout antigo quanto o novo layout com colunas em inglês).
        // A leitura é por NOME do cabeçalho, não por posição — resiste a mudanças de
        // ordem/novas colunas na planilha de origem.
        private static readonly Dictionary<string, string[]> RequiredHeaderAliases = new()
        {
            ["Invoice"] = ["Invoice Number", "Invoice No", "Invoice", "Nº Invoice", "Numero Invoice"],
            ["Codigo"] = ["Item Number", "Código", "Codigo", "Product Code", "SKU"],
            ["Descricao"] = ["Product Name", "Descrição Anvisa", "Descricao Anvisa", "Descrição", "Descricao", "Description", "Produto"],
            ["QtdInvoice"] = ["Quantity Packed", "Qtd Invoice", "Quantidade", "Qty", "Quantity"],
            ["Lote"] = ["Lot Number", "Lote", "Lot"],
            ["Validade"] = ["Lot Expiration Date", "Validade", "Expiration Date", "Data de Validade", "Data Validade"],
            ["Lpn"] = ["LPN"],
        };

        // Colunas opcionais: se não existirem na planilha, a importação segue normalmente.
        //  - RegistroAnvisa: fica vazio quando a coluna não existe.
        //  - PrecisaEtiqueta: filtro — se existir, só importa linhas marcadas com um valor
        //    afirmativo; se não existir (formato antigo), todas as linhas válidas entram.
        private static readonly Dictionary<string, string[]> OptionalHeaderAliases = new()
        {
            ["RegistroAnvisa"] = ["Register# Nº Registro", "Registro Anvisa", "Registro", "Register Number", "Nº Registro", "Register#"],
            ["PrecisaEtiqueta"] = ["Precisa de Etiqueta?", "Precisa de Etiqueta", "Needs Label", "Need Label?", "Necessita Etiqueta?"],
            //  - Local: unidade de destino (Extrema / Palhoça), usada no relatório por invoice.
            ["Local"] = ["Bill To Location City", "Location City", "Cidade", "Filial", "Unidade", "Local"],
        };

        private static readonly string[] AffirmativeValues = ["yes", "sim", "y", "s", "true", "1"];

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
                // FileShare.ReadWrite: permite importar mesmo com a planilha aberta no Excel.
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var workbook = new XLWorkbook(stream);

                // Sempre a primeira aba da planilha, qualquer que seja o nome dela.
                var sheet = workbook.Worksheets.OrderBy(w => w.Position).FirstOrDefault();
                if (sheet is null)
                    return ImportResult.Fail("A planilha não tem nenhuma aba.");

                var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
                var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 1;

                if (lastRow < 2)
                    return ImportResult.Fail("Planilha sem dados (apenas cabeçalho ou vazia).");

                var cols = ResolveHeaders(sheet, lastColumn, out var missingField, out var foundHeaders);
                if (missingField is not null)
                {
                    return ImportResult.Fail(
                        $"Coluna obrigatória '{missingField}' não encontrada no cabeçalho da planilha. " +
                        $"Cabeçalhos encontrados: {string.Join(", ", foundHeaders)}");
                }

                var precisaEtiquetaCol = cols.TryGetValue("PrecisaEtiqueta", out var peCol) ? (int?)peCol : null;
                LogHeaderResolved(logger, precisaEtiquetaCol.HasValue);

                var records = new List<LabelRecord>();
                var skipped = new List<SkippedRow>();
                var totalRows = 0;

                // Numeração do Item recalculada por invoice (1, 2, 3… em cada uma),
                // ignorando a coluna Item da planilha.
                var itemPorInvoice = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                for (int row = 2; row <= lastRow; row++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Linhas totalmente vazias (formatação sobrando no Excel) são ignoradas em silêncio.
                    if (cols.Values.All(c => string.IsNullOrWhiteSpace(GetString(sheet, row, c))))
                        continue;

                    totalRows++;

                    if (TryParseRow(sheet, row, cols, precisaEtiquetaCol,
                        out var record, out var error, out var filteredOut))
                    {
                        var seq = itemPorInvoice.GetValueOrDefault(record!.Invoice) + 1;
                        itemPorInvoice[record.Invoice] = seq;
                        record.Item = seq; // Só conta linhas válidas e importadas
                        records.Add(record);
                        continue;
                    }

                    var codigo = GetString(sheet, row, cols["Codigo"]);

                    if (filteredOut)
                    {
                        skipped.Add(new SkippedRow(row, codigo, "Marcada como \"não precisa de etiqueta\"."));
                    }
                    else
                    {
                        skipped.Add(new SkippedRow(row, codigo, error!));
                        LogRowSkipped(logger, row, error!);
                    }
                }

                if (records.Count == 0)
                    return ImportResult.Fail(
                        "Nenhuma linha válida para importar. A fila atual foi mantida.", skipped);

                int batchId = await repository.CreateImportBatchAsync(
                    filePath, total: totalRows, imported: records.Count, skipped: skipped.Count);

                await repository.ReplaceAllAsync(records, batchId);

                LogImportFinished(logger, totalRows, records.Count, skipped.Count);

                return ImportResult.Ok(totalRows, records, skipped);
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

        // ── Resolução de cabeçalho por nome ───────────────────────────────────

        private static Dictionary<string, int> ResolveHeaders(
            IXLWorksheet sheet, int lastColumn, out string? missingField, out IReadOnlyList<string> foundHeaders)
        {
            var headerLookup = new Dictionary<string, int>();
            var found = new List<string>();

            for (int col = 1; col <= lastColumn; col++)
            {
                var raw = sheet.Cell(HeaderRow, col).GetValue<string>() ?? string.Empty;
                var normalized = NormalizeHeader(raw);
                if (normalized.Length == 0)
                    continue;

                headerLookup.TryAdd(normalized, col);
                found.Add(raw.Trim());
            }

            var resolved = new Dictionary<string, int>();

            int? FindColumn(string[] aliases) => aliases
                .Select(alias => headerLookup.TryGetValue(NormalizeHeader(alias), out var c) ? c : (int?)null)
                .FirstOrDefault(c => c.HasValue);

            foreach (var (field, aliases) in RequiredHeaderAliases)
            {
                var col = FindColumn(aliases);

                if (col is null)
                {
                    missingField = field;
                    foundHeaders = found;
                    return resolved;
                }

                resolved[field] = col.Value;
            }

            foreach (var (field, aliases) in OptionalHeaderAliases)
            {
                if (FindColumn(aliases) is int col)
                    resolved[field] = col;
            }

            missingField = null;
            foundHeaders = found;
            return resolved;
        }

        private static string NormalizeHeader(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return Regex.Replace(text, @"\s+", " ").Trim().ToLowerInvariant();
        }

        // ── Parse de linha individual ─────────────────────────────────────────

        private static bool TryParseRow(
            IXLWorksheet sheet,
            int rowNum,
            IReadOnlyDictionary<string, int> cols,
            int? precisaEtiquetaCol,
            out LabelRecord? record,
            out string? error,
            out bool filteredOut)
        {
            record = null;
            error = null;
            filteredOut = false;

            try
            {
                if (precisaEtiquetaCol is int peCol)
                {
                    var precisa = GetString(sheet, rowNum, peCol);
                    if (!AffirmativeValues.Contains(precisa.Trim(), StringComparer.OrdinalIgnoreCase))
                    {
                        filteredOut = true;
                        return false;
                    }
                }

                var invoice = GetString(sheet, rowNum, cols["Invoice"]);
                if (string.IsNullOrWhiteSpace(invoice))
                { error = "Coluna 'Invoice' está vazia."; return false; }

                var codigo = GetString(sheet, rowNum, cols["Codigo"]);
                if (string.IsNullOrWhiteSpace(codigo))
                { error = "Coluna 'Código' está vazia."; return false; }

                var descricao = GetString(sheet, rowNum, cols["Descricao"]);

                if (!TryGetInt(sheet, rowNum, cols["QtdInvoice"], out int qtd))
                {
                    error = $"Coluna 'Qtd Invoice' inválida ('{GetRaw(sheet, rowNum, cols["QtdInvoice"])}').";
                    return false;
                }

                var lote = GetString(sheet, rowNum, cols["Lote"]);
                if (string.IsNullOrWhiteSpace(lote))
                { error = "Coluna 'Lote' está vazia."; return false; }

                if (!TryGetDate(sheet, rowNum, cols["Validade"], out DateTime validade))
                {
                    error = $"Coluna 'Validade' inválida ('{GetRaw(sheet, rowNum, cols["Validade"])}').";
                    return false;
                }

                // Opcional: vazio quando a planilha não tem a coluna de registro.
                var registroAnvisa = cols.TryGetValue("RegistroAnvisa", out var regCol)
                    ? GetString(sheet, rowNum, regCol)
                    : string.Empty;

                var lpn = GetString(sheet, rowNum, cols["Lpn"]);
                if (string.IsNullOrWhiteSpace(lpn))
                { error = "Coluna 'LPN' está vazia."; return false; }

                var local = cols.TryGetValue("Local", out var localCol)
                    ? NormalizarLocal(GetString(sheet, rowNum, localCol))
                    : string.Empty;

                record = new LabelRecord
                {
                    Invoice = invoice,
                    Codigo = codigo,
                    DescricaoAnvisa = descricao,
                    QtdInvoice = qtd,
                    Lote = lote,
                    Validade = validade,
                    RegistroAnvisa = registroAnvisa,
                    Lpn = lpn,
                    Local = local,
                    LabelFilePath = null,
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

        /// <summary>
        /// Padroniza a unidade: "PALHOCA", "Palhoça"… → "Palhoça"; "EXTREMA" → "Extrema".
        /// Outros valores são mantidos como vieram (vazio = não informado).
        /// </summary>
        private static string NormalizarLocal(string raw)
        {
            if (raw.Contains("PALHO", StringComparison.OrdinalIgnoreCase)) return "Palhoça";
            if (raw.Contains("EXTREMA", StringComparison.OrdinalIgnoreCase)) return "Extrema";
            return raw;
        }

        private static string GetString(IXLWorksheet sheet, int row, int col)
        {
            var raw = sheet.Cell(row, col).GetValue<string>() ?? string.Empty;
            return Regex.Replace(raw, @"\s+", " ").Trim();
        }

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

        [LoggerMessage(Level = LogLevel.Information, Message = "Cabeçalhos resolvidos. Filtro 'Precisa de Etiqueta' ativo: {FiltroAtivo}")]
        private static partial void LogHeaderResolved(ILogger logger, bool filtroAtivo);

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
