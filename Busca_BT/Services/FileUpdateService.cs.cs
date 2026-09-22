using Busca_BT.Data;
using Busca_BT.Models;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Security.Cryptography;

namespace Busca_BT.Services;

public enum AtualizacaoResultado
{
    Atualizado,
    Identico,
    ArquivoInvalido
}

public sealed record AtualizacaoResult(
    AtualizacaoResultado Resultado,
    string Mensagem);

public interface IFileUpdateService
{
    Task<AtualizacaoResult> AtualizarTemplateAsync(
        LabelRecord label,
        string caminhoNovoArquivo,
        CancellationToken ct = default);
}

public sealed class FileUpdateService : IFileUpdateService
{
    private readonly ILabelRepository _repository;
    private readonly EtiquetasOptions _options;
    private readonly ILogger<FileUpdateService> _logger;

    public FileUpdateService(
        ILabelRepository repository,
        EtiquetasOptions options,
        ILogger<FileUpdateService> logger)
    {
        _repository = repository;
        _options = options;
        _logger = logger;
    }

    public async Task<AtualizacaoResult> AtualizarTemplateAsync(
        LabelRecord label,
        string caminhoNovoArquivo,
        CancellationToken ct = default)
    {
        // 1. Valida o arquivo enviado
        if (!File.Exists(caminhoNovoArquivo))
            return new(AtualizacaoResultado.ArquivoInvalido, "Arquivo selecionado não encontrado.");

        var extensao = Path.GetExtension(caminhoNovoArquivo);
        if (!extensao.Equals(".btw", StringComparison.OrdinalIgnoreCase))
            return new(AtualizacaoResultado.ArquivoInvalido, "Apenas arquivos .btw são aceitos.");

        // 2. Calcula o hash do novo arquivo
        var hashNovo = await ComputeHashAsync(caminhoNovoArquivo, ct);

        // 3. Compara com o hash atual
        if (!string.IsNullOrEmpty(label.HashArquivo) &&
            string.Equals(label.HashArquivo, hashNovo, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Template idêntico ao atual. Código={Codigo}", label.Codigo);
            return new(AtualizacaoResultado.Identico,
                "O arquivo enviado é idêntico ao template atual. Nenhuma alteração foi feita.");
        }

        // 4. Define os caminhos
        var nomeArquivo = $"{label.Codigo}.btw";
        var destinoFinal = Path.Combine(_options.PastaArquivos, nomeArquivo);
        var pastaBackup = Path.Combine(_options.PastaArquivos, "Backup");
        var destinoBackup = Path.Combine(pastaBackup,
            $"{label.Codigo}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.btw");

        // 5. Faz backup do arquivo atual (se existir)
        if (File.Exists(destinoFinal))
        {
            Directory.CreateDirectory(pastaBackup);
            File.Copy(destinoFinal, destinoBackup);
            _logger.LogInformation(
                "Backup criado: {BackupPath}", destinoBackup);
        }

        // 6. Copia o novo arquivo pro servidor
        File.Copy(caminhoNovoArquivo, destinoFinal, overwrite: true);

        // 7. Atualiza hash e caminho no banco para todos os registros do mesmo Código
        await _repository.AtualizarHashPorCodigoAsync(label.Codigo, destinoFinal, hashNovo, ct);

        _logger.LogInformation(
            "Template atualizado. Código={Codigo} | Hash={Hash}", label.Codigo, hashNovo);

        return new(AtualizacaoResultado.Atualizado,
            $"Template atualizado com sucesso.\nCódigo: {label.Codigo}");
    }

    private static async Task<string> ComputeHashAsync(
        string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var bytes = await sha256.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}