using Busca_BT.Infrastructure;
using Busca_BT.Models;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Runtime.InteropServices;

namespace Busca_BT.Services
{
    public interface IInvoiceReportService
    {
        /// <summary>
        /// Gera um .xlsx por invoice (título com local e folhas de espelho, cabeçalho e
        /// linhas) numa pasta nova dentro de Downloads\Relatórios PlusBT.
        /// </summary>
        Task<InvoiceReportResult> GerarAsync(IReadOnlyList<LabelRecord> records, string planilhaOrigem);
    }

    public sealed record InvoiceReportResult(string Pasta, IReadOnlyList<string> Arquivos);

    public sealed partial class InvoiceReportService(ILogger<InvoiceReportService> logger) : IInvoiceReportService
    {
        private const string PastaRaiz = "Relatórios PlusBT";

        // Ordem exata das colunas do relatório.
        private static readonly string[] Cabecalhos =
        [
            "Item", "Invoice", "Código", "Descrição ANVISA", "Qtd Invoice",
            "Lote", "Validade", "Registro ANVISA", "LPN"
        ];

        // Larguras pensadas para A4 retrato (a descrição quebra linha; os cabeçalhos também).
        private static readonly double[] Larguras = [5, 15, 10, 32, 7, 11, 10.5, 13, 14];

        public Task<InvoiceReportResult> GerarAsync(IReadOnlyList<LabelRecord> records, string planilhaOrigem)
            => Task.Run(() => Gerar(records, planilhaOrigem));

        private InvoiceReportResult Gerar(IReadOnlyList<LabelRecord> records, string planilhaOrigem)
        {
            var geradoEm = DateTime.Now;
            var pasta = Path.Combine(GetDownloadsFolder(), PastaRaiz, geradoEm.ToString("yyyy-MM-dd HH'h'mm'm'ss's'"));
            Directory.CreateDirectory(pasta);

            var arquivos = new List<string>();

            foreach (var grupo in records.GroupBy(r => r.Invoice, StringComparer.OrdinalIgnoreCase))
            {
                var linhas = grupo.OrderBy(r => r.Item).ToList();
                var caminho = Path.Combine(pasta, $"Invoice {NomeSeguro(grupo.Key)}.xlsx");

                using var workbook = new XLWorkbook();
                PreencherAba(workbook, grupo.Key, linhas, Path.GetFileName(planilhaOrigem), geradoEm);
                workbook.SaveAs(caminho);

                arquivos.Add(caminho);
            }

            LogReportsGenerated(logger, arquivos.Count, pasta);
            return new InvoiceReportResult(pasta, arquivos);
        }

        private static void PreencherAba(
            XLWorkbook workbook, string invoice, IReadOnlyList<LabelRecord> linhas, string origem, DateTime geradoEm)
        {
            var ws = workbook.Worksheets.Add(NomeAba(invoice));
            var ultimaColuna = Cabecalhos.Length;

            var local = linhas.Select(l => l.Local).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            var folhas = EspelhoCalculator.CalcularFolhas(linhas.Count);

            // ── Título ────────────────────────────────────────────────────
            var titulo = ws.Range(1, 1, 1, ultimaColuna).Merge();
            titulo.Value = $"RELATÓRIO DE INVOICE — {invoice}";
            titulo.Style.Font.SetBold().Font.SetFontSize(16)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            ws.Row(1).Height = 28;

            // ── Resumo: local, folhas de espelho, etiquetas ───────────────
            EscreverResumo(ws, 2, 1, "Local", string.IsNullOrWhiteSpace(local) ? "Não informado" : local.ToUpperInvariant());
            EscreverResumo(ws, 2, 4, "Folhas de espelho", folhas);
            EscreverResumo(ws, 2, 6, "Etiquetas", linhas.Count);

            var rodape = ws.Range(3, 1, 3, ultimaColuna).Merge();
            rodape.Value = $"Planilha de origem: {origem}   ·   Gerado em {geradoEm:dd/MM/yyyy HH:mm}";
            rodape.Style.Font.SetFontSize(9).Font.SetFontColor(XLColor.Gray);

            // ── Cabeçalho da tabela ───────────────────────────────────────
            const int linhaCabecalho = 5;
            for (int c = 0; c < Cabecalhos.Length; c++)
                ws.Cell(linhaCabecalho, c + 1).Value = Cabecalhos[c];

            var cabecalho = ws.Range(linhaCabecalho, 1, linhaCabecalho, ultimaColuna);
            cabecalho.Style.Font.SetBold().Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#1F4E79"))
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            cabecalho.Style.Alignment.SetWrapText()
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Row(linhaCabecalho).Height = 30;

            // ── Linhas ────────────────────────────────────────────────────
            var r = linhaCabecalho + 1;
            foreach (var l in linhas)
            {
                ws.Cell(r, 1).Value = l.Item;
                ws.Cell(r, 2).Value = l.Invoice;
                ws.Cell(r, 3).Value = l.Codigo;
                ws.Cell(r, 4).Value = l.DescricaoAnvisa;
                ws.Cell(r, 5).Value = l.QtdInvoice;
                ws.Cell(r, 6).Value = l.Lote;
                ws.Cell(r, 7).Value = l.Validade;
                ws.Cell(r, 7).Style.DateFormat.Format = "dd/MM/yyyy";
                ws.Cell(r, 8).Value = l.RegistroAnvisa;
                ws.Cell(r, 9).Value = l.Lpn;

                // Texto para não virar número/data (lotes como "2027-07-27", códigos com zero à esquerda).
                foreach (var c in new[] { 2, 3, 6, 8, 9 })
                    ws.Cell(r, c).Style.NumberFormat.Format = "@";

                if ((r - linhaCabecalho) % 2 == 0)
                    ws.Range(r, 1, r, ultimaColuna).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F2F6FA"));

                r++;
            }

            var tabela = ws.Range(linhaCabecalho, 1, Math.Max(linhaCabecalho, r - 1), ultimaColuna);
            tabela.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                .Border.SetInsideBorder(XLBorderStyleValues.Thin)
                .Border.SetOutsideBorderColor(XLColor.FromHtml("#BFBFBF"))
                .Border.SetInsideBorderColor(XLColor.FromHtml("#BFBFBF"));
            ws.Range(linhaCabecalho, 1, r - 1, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Range(linhaCabecalho, 5, r - 1, 5).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Range(linhaCabecalho, 7, r - 1, 7).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            for (int c = 0; c < Larguras.Length; c++)
                ws.Column(c + 1).Width = Larguras[c];
            ws.Column(4).Style.Alignment.SetWrapText();
            // Descrição longa quebra em várias linhas: os demais campos ficam centralizados na altura.
            tabela.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);

            // ── Visualização e impressão ──────────────────────────────────
            ws.SheetView.FreezeRows(linhaCabecalho);
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.FitToPages(1, 0); // cabe na largura da folha
            ws.PageSetup.SetRowsToRepeatAtTop(linhaCabecalho, linhaCabecalho);
            ws.PageSetup.Margins.SetLeft(0.3).SetRight(0.3).SetTop(0.5).SetBottom(0.5);
        }

        private static void EscreverResumo(IXLWorksheet ws, int row, int col, string rotulo, XLCellValue valor)
        {
            ws.Cell(row, col).Value = rotulo + ":";
            ws.Cell(row, col).Style.Font.SetFontColor(XLColor.Gray);
            ws.Cell(row, col + 1).Value = valor;
            ws.Cell(row, col + 1).Style.Font.SetBold().Font.SetFontSize(12)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
        }

        // Nome de aba: até 31 caracteres e sem : \ / ? * [ ]
        private static string NomeAba(string invoice)
        {
            var limpo = new string(invoice.Where(ch => !":\\/?*[]".Contains(ch)).ToArray()).Trim();
            if (limpo.Length == 0) limpo = "Invoice";
            return limpo.Length > 31 ? limpo[..31] : limpo;
        }

        private static string NomeSeguro(string nome)
        {
            var invalidos = Path.GetInvalidFileNameChars();
            var limpo = new string(nome.Select(ch => invalidos.Contains(ch) ? '_' : ch).ToArray()).Trim();
            return limpo.Length == 0 ? "sem numero" : limpo;
        }

        // ── Pasta Downloads (respeita redirecionamento do Windows) ──────────

        private static readonly Guid FolderIdDownloads = new("374DE290-123F-4565-9164-39C4925E467B");

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath(
            [MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

        private static string GetDownloadsFolder()
        {
            try
            {
                if (SHGetKnownFolderPath(FolderIdDownloads, 0, IntPtr.Zero, out var ptr) == 0)
                {
                    try { return Marshal.PtrToStringUni(ptr)!; }
                    finally { Marshal.FreeCoTaskMem(ptr); }
                }
            }
            catch
            {
                // cai no fallback abaixo
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "{Count} relatório(s) de invoice gerado(s) em {Pasta}")]
        private static partial void LogReportsGenerated(ILogger logger, int count, string pasta);
    }
}
