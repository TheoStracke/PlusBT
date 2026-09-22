using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace Busca_BT.Services
{
    public interface IBartenderLocator
    {
        Task<string?> FindBartenderAsync();
        Task<bool> ValidateBartenderAsync(string exePath);
    }

    public sealed partial class BartenderLocator(ILogger<BartenderLocator> logger) : IBartenderLocator
    {
        private const string RegistryKeyBartender = @"HKEY_LOCAL_MACHINE\SOFTWARE\Seagull\BarTender";
        private const string RegistryValuePath = "Path";

        public async Task<string?> FindBartenderAsync()
        {
            var candidates = GetCandidatePaths().ToList();

            LogSearchingFor(logger, string.Join("; ", candidates));

            foreach (var path in candidates)
            {
                if (await ValidateBartenderAsync(path))
                {
                    LogFound(logger, path);
                    return path;
                }

                LogNotFound(logger, path);
            }

            LogNoValidInstallation(logger);
            return null;
        }

        public async Task<bool> ValidateBartenderAsync(string exePath)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                    return false;

                try
                {
                    var info = FileVersionInfo.GetVersionInfo(exePath);
                    return info.ProductName?.Contains("BarTender") == true;
                }
                catch
                {
                    return false;
                }
            });
        }

        private IEnumerable<string> GetCandidatePaths()
        {
            // Tentar registro do Windows primeiro
            var registryPath = TryGetFromRegistry();
            if (!string.IsNullOrEmpty(registryPath))
                yield return registryPath;

            // Locais padrão por versão
            var versions = new[] { "Suite", "2023", "2022", "2021", "2020", "2019", "2018", "2017", "2016", "2015" };
            foreach (var version in versions)
            {
                yield return $@"C:\Program Files\Seagull\BarTender {version}\bartender.exe";
                yield return $@"C:\Program Files\Seagull\BarTender {version}\bartend.exe";
                yield return $@"C:\Program Files (x86)\Seagull\BarTender {version}\bartender.exe";
                yield return $@"C:\Program Files (x86)\Seagull\BarTender {version}\bartend.exe";
            }

            yield return @"C:\Program Files\Seagull\BarTender Suite\bartender.exe";
            yield return @"C:\Program Files\Seagull\BarTender Suite\bartend.exe";
            yield return @"C:\Program Files (x86)\Seagull\BarTender Suite\bartender.exe";
            yield return @"C:\Program Files (x86)\Seagull\BarTender Suite\bartend.exe";

            // Procurar em unidades de programas personalizadas
            var drives = DriveInfo.GetDrives();
            foreach (var drive in drives)
            {
                if (drive.IsReady)
                {
                    var searchPath = $@"{drive.Name}Seagull\BarTender";
                    if (Directory.Exists(searchPath))
                    {
                        var matches = Directory.GetFiles(searchPath, "bartender.exe", SearchOption.AllDirectories)
                            .Concat(Directory.GetFiles(searchPath, "bartend.exe", SearchOption.AllDirectories))
                            .FirstOrDefault();
                        if (matches != null)
                            yield return matches;
                    }
                }
            }
        }

        private string? TryGetFromRegistry()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(RegistryKeyBartender);
                if (key is null)
                {
                    LogRegistryKeyNotFound(logger, RegistryKeyBartender);
                    return null;
                }

                var path = key.GetValue(RegistryValuePath) as string;
                if (!string.IsNullOrEmpty(path))
                {
                    var exePaths = new[]
                    {
                        System.IO.Path.Combine(path, "bartender.exe"),
                        System.IO.Path.Combine(path, "bartend.exe")
                    };

                    foreach (var exePath in exePaths)
                    {
                        if (File.Exists(exePath))
                            return exePath;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                LogRegistryError(logger, ex);
                return null;
            }
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "Procurando Bartender em: {Paths}")]
        private static partial void LogSearchingFor(ILogger logger, string paths);

        [LoggerMessage(Level = LogLevel.Information, Message = "Bartender encontrado em: {Path}")]
        private static partial void LogFound(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Bartender não encontrado em: {Path}")]
        private static partial void LogNotFound(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Nenhuma instalação válida do Bartender foi encontrada")]
        private static partial void LogNoValidInstallation(ILogger logger);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Chave de registro não encontrada: {Key}")]
        private static partial void LogRegistryKeyNotFound(ILogger logger, string key);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Erro ao acessar registro do Bartender")]
        private static partial void LogRegistryError(ILogger logger, Exception ex);
    }
}
