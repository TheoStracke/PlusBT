using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;

namespace Busca_BT.Services
{
    public static class BartenderHelper
    {
        /// <summary>
        /// Resolve um atalho .lnk para o caminho do executável usando PowerShell
        /// </summary>
        public static string? ResolveShortcutTarget(string shortcutPath, ILogger logger)
        {
            try
            {
                logger.LogInformation("Resolvendo atalho: {ShortcutPath}", shortcutPath);

                if (!File.Exists(shortcutPath))
                {
                    logger.LogWarning("Atalho não encontrado: {ShortcutPath}", shortcutPath);
                    return null;
                }

                var escapedShortcut = shortcutPath.Replace("'", "''");
                var psScript = $"(New-Object -ComObject WScript.Shell).CreateShortcut('{escapedShortcut}').TargetPath";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NoLogo -Command \"{psScript.Replace("\"", "\\\"")}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process is null)
                {
                    logger.LogWarning("Falha ao iniciar PowerShell");
                    return null;
                }

                var targetPath = process.StandardOutput.ReadToEnd().Trim();
                var errors = process.StandardError.ReadToEnd().Trim();
                process.WaitForExit();

                if (!string.IsNullOrWhiteSpace(errors))
                    logger.LogDebug("PowerShell stderr: {Errors}", errors);

                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    logger.LogWarning("PowerShell não retornou um caminho válido");
                    return null;
                }

                logger.LogInformation("Atalho aponta para: {TargetPath}", targetPath);

                var resolvedPath = ResolveBartenderExecutable(targetPath, logger);
                if (resolvedPath is not null)
                    return resolvedPath;

                logger.LogWarning("Arquivo não encontrado em: {TargetPath}", targetPath);
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao resolver atalho do Bartender");
                return null;
            }
        }

        /// <summary>
        /// Lista todos os atalhos do Bartender no Menu Iniciar
        /// </summary>
        public static IEnumerable<string> FindBartenderShortcuts(ILogger logger)
        {
            var result = new List<string>();

            try
            {
                var startMenuPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Microsoft", "Windows", "Start Menu", "Programs");

                logger.LogInformation("Procurando atalhos em: {Path}", startMenuPath);

                if (!Directory.Exists(startMenuPath))
                {
                    logger.LogWarning("Menu Iniciar não encontrado em: {Path}", startMenuPath);
                    return result;
                }

                var shortcuts = Directory.EnumerateFiles(startMenuPath, "*BarTender*.lnk", SearchOption.AllDirectories);
                foreach (var shortcut in shortcuts)
                {
                    logger.LogInformation("Atalho encontrado: {Shortcut}", shortcut);
                    result.Add(shortcut);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao procurar atalhos do Bartender");
            }

            return result;
        }

        private static string? ResolveBartenderExecutable(string targetPath, ILogger logger)
        {
            if (File.Exists(targetPath) && targetPath.EndsWith("bartend.exe", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Executável do Bartender encontrado: {TargetPath}", targetPath);
                return targetPath;
            }

            var directory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return null;

            logger.LogInformation("Procurando bartend.exe na pasta do atalho: {Directory}", directory);

            var localMatch = Directory.EnumerateFiles(directory, "bartend.exe", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(localMatch))
            {
                logger.LogInformation("bartend.exe encontrado próximo ao atalho: {Path}", localMatch);
                return localMatch;
            }

            var parent = Directory.GetParent(directory)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                logger.LogInformation("Procurando bartend.exe na pasta pai: {Parent}", parent);
                var parentMatch = Directory.EnumerateFiles(parent, "bartend.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(parentMatch))
                {
                    logger.LogInformation("bartend.exe encontrado na pasta pai: {Path}", parentMatch);
                    return parentMatch;
                }
            }

            return null;
        }
    }
}
