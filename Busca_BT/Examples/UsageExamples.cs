using Busca_BT.Data;
using Busca_BT.Models;
using Busca_BT.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.IO;

namespace Busca_BT.Examples
{
    /// <summary>
    /// Exemplo de ViewModel que orquestra os três serviços.
    /// Adapte para WPF (INotifyPropertyChanged) ou WinForms.
    /// </summary>
    public class LabelManagementViewModel(
        IExcelImportService importService,
        ILabelRepository repository,
        IBartenderService bartenderService)
    {
        // ── Caso 1: Importar planilha ─────────────────────────────────────────

        public async Task<string> OnImportButtonClickAsync(string excelFilePath)
        {
            var result = await importService.ImportAsync(excelFilePath);

            if (!result.Success)
                return $"❌ Erro: {result.ErrorMessage}";

            var summary = $"✅ Importação concluída!\n" +
                          $"   Total:      {result.TotalRows}\n" +
                          $"   Importadas: {result.ImportedRows}\n" +
                          $"   Ignoradas:  {result.SkippedRows}";

            if (result.RowErrors.Count > 0)
                summary += "\n\n⚠️ Erros:\n" + string.Join("\n", result.RowErrors);

            return summary;
        }

        // ── Caso 2: Buscar e filtrar ──────────────────────────────────────────

        public Task<IReadOnlyList<LabelRecord>> OnSearchAsync(string? lote = null, string? lpn = null)
            => repository.GetAllAsync(new LabelFilter { Lote = lote, Lpn = lpn });

        // ── Caso 3: Associar arquivo .btw ─────────────────────────────────────

        public async Task<string> OnAssociateBtwFileAsync(int labelId, string btwFilePath)
        {
            var success = await repository.AssociateLabelFileAsync(labelId, btwFilePath);
            return success
                ? $"✅ Arquivo '{Path.GetFileName(btwFilePath)}' associado à etiqueta #{labelId}."
                : $"❌ Etiqueta #{labelId} não encontrada.";
        }

        // ── Caso 4: Abrir no Bartender ────────────────────────────────────────

        public async Task<string> OnOpenInBartenderAsync(int labelId)
        {
            var label = await repository.GetByIdAsync(labelId);
            if (label is null) return $"❌ Etiqueta #{labelId} não encontrada.";

            var result = await bartenderService.OpenLabelAsync(label);
            return result.Success
                ? $"✅ Bartender aberto (PID={result.ProcessId})."
                : $"❌ {result.Message}";
        }

        // ── Caso 5: Impressão com variáveis dinâmicas ─────────────────────────

        public async Task<string> OnPrintWithDataAsync(int labelId, string printerName)
        {
            var label = await repository.GetByIdAsync(labelId);
            if (label is null) return $"❌ Etiqueta #{labelId} não encontrada.";

            var printOptions = new BartenderPrintOptions
            {
                PrinterName = printerName,
                Copies = label.QtdInvoice,
                NamedSubStrings = new Dictionary<string, string>
                {
                    ["LOTE"] = label.Lote,
                    ["VALIDADE"] = label.Validade.ToString("MM/yyyy"),
                    ["CODIGO"] = label.Codigo,
                    ["DESCRICAO"] = label.DescricaoAnvisa,
                    ["REGISTRO_ANVISA"] = label.RegistroAnvisa,
                    ["LPN"] = label.Lpn,
                    ["INVOICE"] = label.Invoice
                }
            };

            var result = await bartenderService.PrintLabelAsync(label, printOptions);

            if (result.Success)
            {
                await repository.UpdateStatusAsync(labelId, LabelStatus.Impressa);
                return $"✅ Etiqueta #{labelId} enviada para impressão (PID={result.ProcessId}).";
            }

            return $"❌ Falha na impressão: {result.Message}";
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Exemplo desativado após a migração para SQL Server
    // ────────────────────────────────────────────────────────────────────────────

    internal static class QuickIntegrationTests
    {
        internal static Task RunAllAsync()
            => Task.CompletedTask;
    }
}