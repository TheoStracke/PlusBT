using Busca_BT.Data;
using Busca_BT.Infrastructure;
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
        // O cabeçalho é procurado nas primeiras linhas: na planilha original ele está na
        // linha 1; no relatório por invoice gerado pelo app, vem depois do título e do resumo.
        private const int MaxLinhaCabecalho = 20;

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

                var headerRow = FindHeaderRow(sheet, lastRow, lastColumn);
                var cols = ResolveHeaders(sheet, headerRow, lastColumn, out var missingField, out var foundHeaders);
                if (missingField is not null)
                {
                    return ImportResult.Fail(
                        $"Coluna obrigatória '{missingField}' não encontrada no cabeçalho da planilha. " +
                        $"Cabeçalhos encontrados: {string.Join(", ", foundHeaders)}");
                }

                if (lastRow <= headerRow)
                    return ImportResult.Fail("Planilha sem dados (apenas cabeçalho ou vazia).");

                // Relatório por invoice: o local fica no resumo acima da tabela ("Local: PALHOÇA").
                var localDoResumo = cols.ContainsKey("Local") ? string.Empty : LerLocalDoResumo(sheet, headerRow);

                var precisaEtiquetaCol = cols.TryGetValue("PrecisaEtiqueta", out var peCol) ? (int?)peCol : null;
                LogHeaderResolved(logger, precisaEtiquetaCol.HasValue);

                var records = new List<LabelRecord>();
                var skipped = new List<SkippedRow>();
                var totalRows = 0;

                // Posição da linha dentro da invoice (1, 2, 3…), ignorando a coluna Item da planilha:
                // desempata itens do mesmo LPN e é renumerada na ordem por LPN mais abaixo.
                var itemPorInvoice = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                for (int row = headerRow + 1; row <= lastRow; row++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Linhas totalmente vazias (formatação sobrando no Excel) são ignoradas em silêncio.
                    if (cols.Values.All(c => string.IsNullOrWhiteSpace(GetString(sheet, row, c))))
                        continue;

                    totalRows++;

                    // Filtro intencional da planilha: a linha não deve virar etiqueta.
                    if (precisaEtiquetaCol is int filtroCol &&
                        !AffirmativeValues.Contains(GetString(sheet, row, filtroCol), StringComparer.OrdinalIgnoreCase))
                    {
                        skipped.Add(new SkippedRow(row, GetString(sheet, row, cols["Codigo"]),
                            "Marcada como \"não precisa de etiqueta\"."));
                        continue;
                    }

                    // Qualquer outro problema (data inválida, campo vazio…) NÃO tira a linha da
                    // fila: ela entra sinalizada com o aviso, para o usuário ver e corrigir.
                    var record = ParseRow(sheet, row, cols, localDoResumo);
                    if (record.Avisos is not null)
                        LogRowWithWarnings(logger, row, record.Avisos);

                    var seq = itemPorInvoice.GetValueOrDefault(record.Invoice) + 1;
                    itemPorInvoice[record.Invoice] = seq;
                    record.Item = seq;
                    records.Add(record);
                }

                if (records.Count == 0)
                    return ImportResult.Fail(
                        "Nenhuma linha válida para importar. A fila atual foi mantida.", skipped);

                // Renumera o Item na ordem em que a Home e o relatório mostram (por LPN),
                // para a coluna Item aparecer 1, 2, 3… na tela.
                foreach (var invoice in records.GroupBy(r => r.Invoice, StringComparer.OrdinalIgnoreCase))
                {
                    var n = 0;
                    foreach (var r in LpnOrdem.Ordenar(invoice))
                        r.Item = ++n;
                }

                // Importação + troca da fila numa transação só: se a conexão cair, nada muda.
                await repository.ReplaceQueueAsync(filePath, total: totalRows, skipped: skipped.Count, records);

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

        /// <summary>
        /// Primeira linha (entre as iniciais) que tem todas as colunas obrigatórias. Sem
        /// nenhuma, devolve a linha 1, para a mensagem de erro listar o cabeçalho de lá.
        /// </summary>
        private static int FindHeaderRow(IXLWorksheet sheet, int lastRow, int lastColumn)
        {
            for (int row = 1; row <= Math.Min(lastRow, MaxLinhaCabecalho); row++)
            {
                ResolveHeaders(sheet, row, lastColumn, out var missingField, out _);
                if (missingField is null)
                    return row;
            }
            return 1;
        }

        /// <summary>Valor ao lado de "Local:" nas linhas acima do cabeçalho (vazio se não houver).</summary>
        private static string LerLocalDoResumo(IXLWorksheet sheet, int headerRow)
        {
            if (headerRow == 1)
                return string.Empty;

            foreach (var cell in sheet.Rows(1, headerRow - 1).CellsUsed())
            {
                if (NormalizeHeader(cell.GetValue<string>()) != "local:")
                    continue;

                var valor = GetString(sheet, cell.Address.RowNumber, cell.Address.ColumnNumber + 1);
                return valor.Equals("Não informado", StringComparison.OrdinalIgnoreCase) ? string.Empty : valor;
            }
            return string.Empty;
        }

        private static Dictionary<string, int> ResolveHeaders(
            IXLWorksheet sheet, int headerRow, int lastColumn, out string? missingField, out IReadOnlyList<string> foundHeaders)
        {
            var headerLookup = new Dictionary<string, int>();
            var found = new List<string>();

            for (int col = 1; col <= lastColumn; col++)
            {
                var raw = sheet.Cell(headerRow, col).GetValue<string>() ?? string.Empty;
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

        // Tamanhos das colunas em dbo.Labels: valor maior seria rejeitado pelo SQL e
        // derrubaria a importação inteira, então é cortado e sinalizado.
        private const int MaxTexto = 100;
        private const int MaxDescricao = 500;
        private const int MaxAvisos = 1000;

        /// <summary>Texto usado quando a planilha não traz o número da invoice.</summary>
        public const string SemInvoice = "SEM INVOICE";

        /// <summary>
        /// Lê uma linha sempre produzindo um registro. Cada problema encontrado vira um
        /// aviso em <see cref="LabelRecord.Avisos"/> em vez de descartar a linha.
        /// </summary>
        private static LabelRecord ParseRow(IXLWorksheet sheet, int rowNum, IReadOnlyDictionary<string, int> cols, string localPadrao)
        {
            var avisos = new List<string>();

            string Texto(string campo, string nome, int max = MaxTexto, bool obrigatorio = true)
            {
                if (!cols.TryGetValue(campo, out var col))
                    return string.Empty;

                string valor;
                try { valor = GetString(sheet, rowNum, col); }
                catch (Exception ex)
                {
                    avisos.Add($"{nome}: não foi possível ler a célula ({ex.Message}).");
                    return string.Empty;
                }

                if (obrigatorio && valor.Length == 0)
                    avisos.Add($"{nome} vazio na planilha.");

                if (valor.Length > max)
                {
                    avisos.Add($"{nome} com mais de {max} caracteres foi cortado.");
                    valor = valor[..max];
                }

                return valor;
            }

            var invoice = Texto("Invoice", "Invoice");
            if (invoice.Length == 0)
                invoice = SemInvoice;

            var record = new LabelRecord
            {
                Invoice = invoice,
                Codigo = Texto("Codigo", "Código"),
                DescricaoAnvisa = Texto("Descricao", "Descrição", MaxDescricao, obrigatorio: false),
                Lote = Texto("Lote", "Lote"),
                RegistroAnvisa = Texto("RegistroAnvisa", "Registro ANVISA", obrigatorio: false),
                Lpn = Texto("Lpn", "LPN"),
                Local = NormalizarLocal(cols.ContainsKey("Local") ? Texto("Local", "Local", obrigatorio: false) : localPadrao),
                LabelFilePath = null,
                ImportedAt = DateTime.UtcNow
            };

            if (TryGetInt(sheet, rowNum, cols["QtdInvoice"], out int qtd) && qtd > 0)
            {
                record.QtdInvoice = qtd;
            }
            else
            {
                var raw = SafeRaw(sheet, rowNum, cols["QtdInvoice"]);
                avisos.Add(raw.Length == 0
                    ? "Qtd Invoice vazia na planilha."
                    : $"Qtd Invoice inválida na planilha ('{raw}').");
            }

            if (TryGetDate(sheet, rowNum, cols["Validade"], out var validade))
            {
                record.Validade = validade;
            }
            else
            {
                var raw = SafeRaw(sheet, rowNum, cols["Validade"]);
                // "—" é como o relatório por invoice mostra a validade que veio vazia.
                if (raw.Length == 0 || raw == "—")
                {
                    avisos.Add("Validade vazia na planilha.");
                }
                else
                {
                    record.ValidadeTexto = raw.Length > MaxTexto ? raw[..MaxTexto] : raw;
                    avisos.Add($"Validade inválida na planilha ('{raw}'): não é uma data que existe.");
                }
            }

            if (avisos.Count > 0)
            {
                var texto = string.Join("\n", avisos);
                record.Avisos = texto.Length > MaxAvisos ? texto[..MaxAvisos] : texto;
            }

            return record;
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
            // Remove caracteres invisíveis (zero-width space, BOM…) que vêm de copiar/colar
            // e fazem um código "igual" não bater com o template.
            raw = InvisibleChars().Replace(raw, string.Empty);
            return Regex.Replace(raw, @"\s+", " ").Trim();
        }

        [GeneratedRegex(@"\p{Cf}")]
        private static partial Regex InvisibleChars();

        private static string SafeRaw(IXLWorksheet sheet, int row, int col)
        {
            try { return (sheet.Cell(row, col).Value.ToString() ?? string.Empty).Trim(); }
            catch { return string.Empty; }
        }

        private static bool TryGetInt(IXLWorksheet sheet, int row, int col, out int value)
        {
            value = 0;
            try
            {
                var cell = sheet.Cell(row, col);
                if (cell.Value.IsNumber)
                {
                    var d = cell.GetValue<double>();
                    if (d != Math.Floor(d) || d > int.MaxValue) return false;
                    value = (int)d;
                    return true;
                }
                // Aceita "1.200" / "1 200" (separador de milhar) além de "1200".
                var texto = Regex.Replace(cell.GetValue<string>() ?? string.Empty, @"[\s.]", "");
                return int.TryParse(texto, out value);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetDate(IXLWorksheet sheet, int row, int col, out DateTime value)
        {
            value = default;
            try
            {
                var cell = sheet.Cell(row, col);

                if (cell.Value.IsDateTime)
                    value = cell.GetValue<DateTime>();
                else if (cell.Value.IsNumber)
                    value = DateTime.FromOADate(cell.GetValue<double>());
                else if (!TryParseDateText(cell.GetValue<string>(), out value))
                    return false;
            }
            catch
            {
                return false;
            }

            // Ano absurdo (ex.: "31/07/0228") é erro de digitação, não uma validade.
            return value.Year is >= 2000 and <= 2100;
        }

        private static bool TryParseDateText(string? text, out DateTime value)
        {
            value = default;
            var raw = text?.Trim();
            if (string.IsNullOrEmpty(raw))
                return false;

            // Ignora hora no fim ("31/07/2028 00:00:00") e aceita "-" ou "." como separador.
            raw = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Replace('-', '/').Replace('.', '/');

            // Dia/mês (padrão brasileiro) primeiro; depois ISO e, por último, mês/dia americano.
            string[] formats = ["d/M/yyyy", "d/M/yy", "yyyy/M/d", "M/d/yyyy"];
            return DateTime.TryParseExact(raw, formats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out value);
        }

        // ── LoggerMessage source generators (CA1848) ─────────────────────────

        [LoggerMessage(Level = LogLevel.Information, Message = "Iniciando importação de: {FilePath}")]
        private static partial void LogImportStarted(ILogger logger, string filePath);

        [LoggerMessage(Level = LogLevel.Information, Message = "Cabeçalhos resolvidos. Filtro 'Precisa de Etiqueta' ativo: {FiltroAtivo}")]
        private static partial void LogHeaderResolved(ILogger logger, bool filtroAtivo);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Linha {Row} importada com aviso: {Avisos}")]
        private static partial void LogRowWithWarnings(ILogger logger, int row, string avisos);

        [LoggerMessage(Level = LogLevel.Information,
            Message = "Importação concluída. Total={Total} | Importadas={Imported} | Ignoradas={Skipped}")]
        private static partial void LogImportFinished(ILogger logger, int total, int imported, int skipped);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Importação cancelada pelo usuário: {FilePath}")]
        private static partial void LogImportCancelled(ILogger logger, string filePath);

        [LoggerMessage(Level = LogLevel.Error, Message = "Erro inesperado ao importar planilha: {FilePath}")]
        private static partial void LogImportError(ILogger logger, string filePath, Exception ex);
    }
}
