using Busca_BT.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text;
using System.IO;

namespace Busca_BT.Services
{
    // ────────────────────────────────────────────────────────────────────────────
    // Configuração
    // ────────────────────────────────────────────────────────────────────────────

    public class BartenderOptions
    {
        public const string Section = "Bartender";

        public string ExecutablePath { get; set; } =
            @"C:\Program Files\Seagull\BarTender Suite\bartend.exe";

        /// <summary>ms para aguardar WaitForInputIdle após o processo iniciar. 0 = fire-and-forget.</summary>
        public int StartupTimeoutMs { get; set; } = 3000;

        /// <summary>
        /// true = impressão sem UI (requer Automation Edition).
        /// false = abre a interface do Bartender normalmente (Desktop Edition).
        /// </summary>
        public bool SilentPrint { get; set; } = false;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Interface + resultado
    // ────────────────────────────────────────────────────────────────────────────

    public interface IBartenderService
    {
        Task<BartenderResult> OpenLabelAsync(LabelRecord label);
        Task<BartenderResult> PrintLabelAsync(LabelRecord label, BartenderPrintOptions? options = null);
        Task<BartenderResult> OpenPdfAsync(LabelRecord label);
    }

    public sealed record BartenderResult(bool Success, string? Message = null, int? ProcessId = null)
    {
        public static BartenderResult Ok(int pid) => new(true, ProcessId: pid);
        public static BartenderResult Fail(string message) => new(false, message);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Implementação
    // ────────────────────────────────────────────────────────────────────────────

    public sealed partial class BartenderService(
        IOptions<BartenderOptions> options,
        ILogger<BartenderService> logger) : IBartenderService
    {
        private readonly BartenderOptions _options = options.Value;

        // ── Abrir para edição ─────────────────────────────────────────────────

        public async Task<BartenderResult> OpenLabelAsync(LabelRecord label)
        {
            var err = ValidateLabel(label, requireBtw: true);
            if (err is not null) return err;

            LogOpenLabel(logger, label.Id, label.LabelFilePath!);
            return await LaunchBartenderAsync(BuildArguments(label.LabelFilePath!, false, null));
        }

        // ── Impressão via CLI ─────────────────────────────────────────────────

        public async Task<BartenderResult> PrintLabelAsync(LabelRecord label, BartenderPrintOptions? options = null)
        {
            var err = ValidateLabel(label, requireBtw: true);
            if (err is not null) return err;

            options ??= new BartenderPrintOptions();
            LogPrintLabel(logger, label.Id, label.LabelFilePath!, options.PrinterName ?? "(padrão)", options.Copies);
            return await LaunchBartenderAsync(BuildArguments(label.LabelFilePath!, true, options));
        }

        // ── Abrir PDF ─────────────────────────────────────────────────────────

        public Task<BartenderResult> OpenPdfAsync(LabelRecord label)
        {
            var err = ValidateLabel(label, requireBtw: false);
            if (err is not null) return Task.FromResult(err);

            if (!label.IsPdf)
                return Task.FromResult(BartenderResult.Fail("A etiqueta não possui um arquivo PDF associado."));

            LogOpenPdf(logger, label.Id, label.LabelFilePath!);
            return OpenWithShellAsync(label.LabelFilePath!);
        }

        // ── Montagem dos argumentos CLI do Bartender ──────────────────────────
        //
        // Referência: BarTender 2016 Command Line Interface Reference
        //   /F="<path>"       arquivo .btw
        //   /P                imprimir (sem UI, requer Automation)
        //   /C=<n>            cópias
        //   /PRN="<name>"     impressora lógica
        //   /NSS="K\tV\nK\tV" NamedSubStrings (variáveis da etiqueta)
        //   /QUIT             fechar ao terminar
        //
        private string BuildArguments(string filePath, bool printMode, BartenderPrintOptions? opts)
        {
            var sb = new StringBuilder();
            sb.Append($"/F=\"{filePath}\"");

            if (printMode && opts is not null)
            {
                if (_options.SilentPrint)
                    sb.Append(" /P");

                if (opts.Copies > 1)
                    sb.Append($" /C={opts.Copies}");

                if (!string.IsNullOrWhiteSpace(opts.PrinterName))
                    sb.Append($" /PRN=\"{opts.PrinterName}\"");

                // /NSS="LOTE\t539271\nVALIDADE\t12/2026"
                if (opts.NamedSubStrings.Count > 0)
                {
                    var nss = string.Join(@"\n",
                        opts.NamedSubStrings.Select(kv => $@"{kv.Key}\t{kv.Value}"));
                    sb.Append($" /NSS=\"{nss}\"");
                }

                if (_options.SilentPrint)
                    sb.Append(" /QUIT");
            }

            return sb.ToString();
        }

        // ── Launch helpers ────────────────────────────────────────────────────

        private async Task<BartenderResult> LaunchBartenderAsync(string arguments)
        {
            if (!File.Exists(_options.ExecutablePath))
            {
                var msg = $"Executável do Bartender não encontrado: {_options.ExecutablePath}";
                LogExeNotFound(logger, _options.ExecutablePath);
                return BartenderResult.Fail(msg);
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _options.ExecutablePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = false
                };

                LogProcessStarting(logger, _options.ExecutablePath, arguments);

                using var process = Process.Start(psi)
                    ?? throw new InvalidOperationException("Process.Start retornou null.");

                if (_options.StartupTimeoutMs > 0)
                    await Task.Run(() => process.WaitForInputIdle(_options.StartupTimeoutMs));

                LogProcessStarted(logger, process.Id);
                return BartenderResult.Ok(process.Id);
            }
            catch (Exception ex)
            {
                LogProcessError(logger, arguments, ex);
                return BartenderResult.Fail($"Não foi possível abrir o Bartender: {ex.Message}");
            }
        }

        private static async Task<BartenderResult> OpenWithShellAsync(string filePath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                };

                using var process = await Task.Run(() => Process.Start(psi));
                return process is null
                    ? BartenderResult.Fail("Não foi possível abrir o arquivo.")
                    : BartenderResult.Ok(process.Id);
            }
            catch (Exception ex)
            {
                return BartenderResult.Fail($"Erro ao abrir o arquivo: {ex.Message}");
            }
        }

        // ── Validação ─────────────────────────────────────────────────────────

        private static BartenderResult? ValidateLabel(LabelRecord label, bool requireBtw)
        {
            if (!label.HasFile)
                return BartenderResult.Fail(
                    $"Nenhum arquivo associado à etiqueta Id={label.Id}. Use 'Associar Arquivo' antes.");

            if (!File.Exists(label.LabelFilePath))
                return BartenderResult.Fail(
                    $"Arquivo não encontrado no disco: {label.LabelFilePath}");

            if (requireBtw && !label.IsBtw)
                return BartenderResult.Fail(
                    $"O arquivo associado não é um .btw: {label.LabelFilePath}");

            return null;
        }

        // ── LoggerMessage source generators (CA1848) ─────────────────────────

        [LoggerMessage(Level = LogLevel.Information,
            Message = "Abrindo etiqueta no Bartender | Id={Id} | Arquivo={FilePath}")]
        private static partial void LogOpenLabel(ILogger logger, int id, string filePath);

        [LoggerMessage(Level = LogLevel.Information,
            Message = "Imprimindo etiqueta | Id={Id} | Arquivo={FilePath} | Impressora={Printer} | Cópias={Copies}")]
        private static partial void LogPrintLabel(ILogger logger, int id, string filePath, string printer, int copies);

        [LoggerMessage(Level = LogLevel.Information,
            Message = "Abrindo PDF com visualizador padrão | Id={Id} | Arquivo={FilePath}")]
        private static partial void LogOpenPdf(ILogger logger, int id, string filePath);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "Executável do Bartender não encontrado: {ExePath}")]
        private static partial void LogExeNotFound(ILogger logger, string exePath);

        [LoggerMessage(Level = LogLevel.Debug,
            Message = "Iniciando processo: \"{ExePath}\" {Arguments}")]
        private static partial void LogProcessStarting(ILogger logger, string exePath, string arguments);

        [LoggerMessage(Level = LogLevel.Information,
            Message = "Bartender iniciado com PID={Pid}")]
        private static partial void LogProcessStarted(ILogger logger, int pid);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "Falha ao iniciar o Bartender. Args: {Arguments}")]
        private static partial void LogProcessError(ILogger logger, string arguments, Exception ex);
    }
}