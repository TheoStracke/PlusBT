using Busca_BT.Data;
using Busca_BT.Infrastructure;
using Busca_BT.Models;
using Microsoft.Extensions.Logging;
using System.IO;

namespace Busca_BT.Services;

public enum StatusArquivo
{
    /// <summary>O banco ainda não tem arquivo para este código.</summary>
    SemArquivoNoBanco,
    /// <summary>Ainda não foi baixado para este PC.</summary>
    NaoBaixado,
    /// <summary>Cópia local igual à versão atual do banco.</summary>
    Sincronizado,
    /// <summary>Há versão mais nova no banco; será baixada na próxima sincronização.</summary>
    Desatualizado,
    /// <summary>Arquivo editado neste PC e ainda não enviado.</summary>
    AlteradoAqui,
    /// <summary>Editado aqui E alguém enviou versão nova enquanto isso.</summary>
    Conflito
}

/// <summary>Template com a situação da cópia local deste PC (tela Templates).</summary>
public sealed class TemplateLocal
{
    public required TemplateInfo Info { get; init; }
    public required StatusArquivo Status { get; init; }
    public string? CaminhoLocal { get; init; }
    public int? VersaoLocal { get; init; }
}

public interface ITemplateArquivos
{
    ModoTemplates Modo { get; }

    /// <summary>Pasta da cópia local do acervo neste PC.</summary>
    string PastaLocal { get; }

    /// <summary>Templates (código → arquivo) para vincular à fila, conforme o modo do PC.</summary>
    IReadOnlyList<LabelRecord> ObterParaVinculo();

    /// <summary>Mensagem de pendência quando o arquivo vinculado não existe neste PC.</summary>
    string MensagemArquivoAusente(string caminho);

    /// <summary>Acervo com a situação da cópia local de cada template (modo Banco).</summary>
    IReadOnlyList<TemplateLocal> ListarComStatus();

    /// <summary>Baixa para a pasta local o que é novo ou mudou. Retorna quantos arquivos foram gravados.</summary>
    Task<int> SincronizarArquivosAsync(IReadOnlyList<TemplateInfo> doBanco, CancellationToken ct = default);

    /// <summary>Antes de abrir: se houver internet e versão mais nova, baixa esta agora.</summary>
    Task GarantirAtualizadoAsync(string caminhoLocal);

    Task<EnvioTemplate> EnviarArquivoAsync(string caminhoOrigem, string origem, string? codigo = null);
    Task<ResumoEnvioPasta> EnviarPastaAsync(string pasta, IProgress<(int Feitos, int Total)>? progresso = null, CancellationToken ct = default);
    Task<EnvioTemplate> EnviarAlteracaoAsync(string codigo, bool forcar);
    Task<IReadOnlyList<TemplateVersao>> ListarVersoesAsync(string codigo);
    Task<EnvioTemplate> RestaurarAsync(string codigo, int versao);
    Task<(int Copiados, int Faltando)> ExportarAsync(string pastaDestino);
}

public sealed record ResumoEnvioPasta(int Criados, int NovasVersoes, int Identicos, int Duplicados, IReadOnlyList<string> Erros);

/// <summary>
/// Acervo de templates: arquivos .btw versionados no banco + cópia local em
/// C:\ProgramData\BuscaBT\Templates. Abrir etiqueta usa sempre a cópia local (funciona
/// offline); só administrador envia arquivos/versões. No modo PastaLocal os templates
/// são lidos direto de uma pasta escolhida e nada é sincronizado.
/// </summary>
public sealed partial class TemplateArquivoService(
    TemplateRepository repo,
    LocalCache cache,
    ISessaoOperador sessao,
    IPreferenciasStore preferencias,
    ILogger<TemplateArquivoService> logger,
    string? pastaLocal = null) : ITemplateArquivos
{
    private const int LoteDownload = 40;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ModoTemplates Modo => preferencias.Atual.ModoTemplates;

    /// <summary>C:\ProgramData\BuscaBT\Templates (outra pasta só em testes).</summary>
    public string PastaLocal { get; } = CriarPastaLocal(pastaLocal);

    private static string CriarPastaLocal(string? pasta)
    {
        pasta ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BuscaBT", "Templates");
        Directory.CreateDirectory(pasta);
        return pasta;
    }

    private string CaminhoLocal(string nomeArquivo) => Path.Combine(PastaLocal, Path.GetFileName(nomeArquivo));

    // ── Vínculo com a fila ───────────────────────────────────────────────────

    public IReadOnlyList<LabelRecord> ObterParaVinculo()
    {
        if (Modo == ModoTemplates.PastaLocal)
        {
            var pasta = preferencias.Atual.PastaTemplates;
            if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta))
                return [];

            return Directory.EnumerateFiles(pasta, "*.btw", SearchOption.AllDirectories)
                .Select(f => new LabelRecord { Codigo = Path.GetFileNameWithoutExtension(f), LabelFilePath = f })
                .ToList();
        }

        return cache.LerTemplates()
            .Select(t => new LabelRecord
            {
                Codigo = t.Codigo,
                LabelFilePath = t.TemArquivoNoBanco ? CaminhoLocal(t.NomeArquivo!) : null
            })
            .ToList();
    }

    public string MensagemArquivoAusente(string caminho) => Modo == ModoTemplates.Banco
        ? "Template ainda não baixado neste PC: será baixado na próxima sincronização com internet."
        : $"Arquivo do template não encontrado: {caminho}";

    // ── Situação da cópia local ──────────────────────────────────────────────

    public IReadOnlyList<TemplateLocal> ListarComStatus()
    {
        var manifesto = cache.LerArquivosLocais();
        return cache.LerTemplates().Select(t => Avaliar(t, manifesto)).ToList();
    }

    private TemplateLocal Avaliar(TemplateInfo t, IReadOnlyDictionary<string, ArquivoLocal> manifesto)
    {
        if (!t.TemArquivoNoBanco)
            return new TemplateLocal { Info = t, Status = StatusArquivo.SemArquivoNoBanco };

        var caminho = CaminhoLocal(t.NomeArquivo!);
        manifesto.TryGetValue(t.Codigo, out var m);

        if (!File.Exists(caminho))
            return new TemplateLocal { Info = t, Status = StatusArquivo.NaoBaixado, CaminhoLocal = caminho };

        // Sem manifesto (ex.: cópia antiga): se o arquivo é igual ao do banco, adota.
        if (m is null)
        {
            if (HashArquivo.Sha256(caminho) == t.HashAtual)
            {
                m = RegistrarManifesto(t.Codigo, t.NomeArquivo!, t.VersaoAtual, t.HashAtual!, caminho);
                return new TemplateLocal { Info = t, Status = StatusArquivo.Sincronizado, CaminhoLocal = caminho, VersaoLocal = m.Versao };
            }
            return new TemplateLocal { Info = t, Status = StatusArquivo.AlteradoAqui, CaminhoLocal = caminho };
        }

        if (FoiEditado(caminho, m))
        {
            var status = t.VersaoAtual > m.Versao ? StatusArquivo.Conflito : StatusArquivo.AlteradoAqui;
            return new TemplateLocal { Info = t, Status = status, CaminhoLocal = caminho, VersaoLocal = m.Versao };
        }

        // Versão local mais nova que a lista em cache = acabou de ser enviada daqui.
        var atualizado = m.Versao > t.VersaoAtual || (m.Versao == t.VersaoAtual && m.Hash == t.HashAtual);
        return new TemplateLocal
        {
            Info = t,
            Status = atualizado ? StatusArquivo.Sincronizado : StatusArquivo.Desatualizado,
            CaminhoLocal = caminho,
            VersaoLocal = m.Versao
        };
    }

    /// <summary>Tamanho/data iguais ao do download → não editado (evita reler o arquivo); senão, compara o hash.</summary>
    private static bool FoiEditado(string caminho, ArquivoLocal m)
    {
        var info = new FileInfo(caminho);
        if (info.Length == m.Tamanho && info.LastWriteTimeUtc.Ticks == m.ModificadoTicks)
            return false;
        return HashArquivo.Sha256(caminho) != m.Hash;
    }

    private ArquivoLocal RegistrarManifesto(string codigo, string nomeArquivo, int versao, string hash, string caminho)
    {
        var info = new FileInfo(caminho);
        var m = new ArquivoLocal(codigo, nomeArquivo, versao, hash, info.Length, info.LastWriteTimeUtc.Ticks);
        cache.SalvarArquivoLocal(m);
        return m;
    }

    /// <summary>Grava o arquivo de forma atômica (temporário + troca) e atualiza o manifesto.</summary>
    private void GravarLocal(string codigo, string nomeArquivo, int versao, string hash, byte[] conteudo)
    {
        var caminho = CaminhoLocal(nomeArquivo);
        var temp = caminho + ".baixando";
        File.WriteAllBytes(temp, conteudo);
        File.Move(temp, caminho, overwrite: true);
        RegistrarManifesto(codigo, nomeArquivo, versao, hash, caminho);
    }

    // ── Download (sincronização) ─────────────────────────────────────────────

    public async Task<int> SincronizarArquivosAsync(IReadOnlyList<TemplateInfo> doBanco, CancellationToken ct = default)
    {
        if (Modo != ModoTemplates.Banco)
            return 0;

        await _lock.WaitAsync(ct);
        try
        {
            var manifesto = cache.LerArquivosLocais();
            var baixar = new List<TemplateInfo>();

            foreach (var t in doBanco)
            {
                var local = Avaliar(t, manifesto);
                switch (local.Status)
                {
                    case StatusArquivo.NaoBaixado:
                    case StatusArquivo.Desatualizado:
                        baixar.Add(t);
                        break;

                    // Alteração local num PC operado pelo perfil Impressão: guarda em _descartados
                    // e volta à versão do banco (protege contra o BarTender salvar sem querer).
                    case StatusArquivo.AlteradoAqui or StatusArquivo.Conflito
                        when sessao.Atual is { IsAdmin: false }:
                        Descartar(local.CaminhoLocal!);
                        baixar.Add(t);
                        break;
                }
            }

            var gravados = 0;
            foreach (var lote in baixar.Chunk(LoteDownload))
            {
                ct.ThrowIfCancellationRequested();
                var conteudos = await repo.BaixarAtuaisAsync(lote.Select(t => t.Id).ToList(), ct);
                foreach (var c in conteudos)
                {
                    if (HashArquivo.Sha256(c.Conteudo) != c.Hash)
                    {
                        LogHashInvalido(logger, c.Codigo, c.Versao);
                        continue;
                    }

                    try
                    {
                        GravarLocal(c.Codigo, c.NomeArquivo, c.Versao, c.Hash, c.Conteudo);
                        gravados++;
                    }
                    catch (IOException ex)
                    {
                        // Arquivo aberto no BarTender, por exemplo: tenta na próxima sincronização.
                        LogNaoGravou(logger, c.Codigo, ex.Message);
                    }
                }
            }

            if (gravados > 0)
                LogBaixados(logger, gravados);
            return gravados;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void Descartar(string caminho)
    {
        try
        {
            var pasta = Path.Combine(PastaLocal, "_descartados");
            Directory.CreateDirectory(pasta);
            var destino = Path.Combine(pasta,
                $"{Path.GetFileNameWithoutExtension(caminho)}_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(caminho)}");
            File.Move(caminho, destino);
            LogDescartado(logger, caminho, destino);
        }
        catch (IOException ex)
        {
            LogNaoGravou(logger, caminho, ex.Message);
        }
    }

    public async Task GarantirAtualizadoAsync(string caminhoLocal)
    {
        if (Modo != ModoTemplates.Banco)
            return;

        var t = cache.LerTemplates().FirstOrDefault(x =>
            x.TemArquivoNoBanco && string.Equals(CaminhoLocal(x.NomeArquivo!), caminhoLocal, StringComparison.OrdinalIgnoreCase));
        if (t is null)
            return;

        try
        {
            await SincronizarArquivosAsync([t]);
        }
        catch (Exception ex) when (ISyncService.IsErroDeConexao(ex))
        {
            // Sem internet: abre a cópia que já está no PC.
        }
    }

    // ── Envio (só administrador) ─────────────────────────────────────────────

    private void ExigirAdmin()
    {
        if (!sessao.IsAdmin)
            throw new InvalidOperationException("Só administradores podem enviar templates.");
    }

    public async Task<EnvioTemplate> EnviarArquivoAsync(string caminhoOrigem, string origem, string? codigo = null)
    {
        ExigirAdmin();

        codigo ??= Path.GetFileNameWithoutExtension(caminhoOrigem);
        // Nova versão de um template existente mantém o nome de arquivo dele.
        var existente = cache.LerTemplates().FirstOrDefault(t => t.Codigo == codigo);
        var nomeArquivo = existente?.NomeArquivo ?? Path.GetFileName(caminhoOrigem);
        var conteudo = LerArquivo(caminhoOrigem);

        var r = await Online("Enviar template", () => repo.EnviarAsync(codigo, nomeArquivo, conteudo, origem));
        if (r.Resultado is ResultadoEnvio.Criado or ResultadoEnvio.NovaVersao or ResultadoEnvio.Identico)
            AtualizarCopiaLocal(codigo, nomeArquivo, r.Versao, conteudo);
        return r;
    }

    public async Task<ResumoEnvioPasta> EnviarPastaAsync(
        string pasta, IProgress<(int Feitos, int Total)>? progresso = null, CancellationToken ct = default)
    {
        ExigirAdmin();

        // Mesmo código em subpastas diferentes: fica o arquivo mais recente.
        var grupos = Directory.EnumerateFiles(pasta, "*.btw", SearchOption.AllDirectories)
            .GroupBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase)
            .ToList();

        int criados = 0, novas = 0, identicos = 0, feitos = 0;
        var duplicados = grupos.Sum(g => g.Count() - 1);
        var erros = new List<string>();

        foreach (var g in grupos)
        {
            ct.ThrowIfCancellationRequested();
            var arquivo = g.OrderByDescending(File.GetLastWriteTimeUtc).First();
            try
            {
                var r = await EnviarArquivoAsync(arquivo, "importacao_pasta", g.Key);
                switch (r.Resultado)
                {
                    case ResultadoEnvio.Criado: criados++; break;
                    case ResultadoEnvio.NovaVersao: novas++; break;
                    default: identicos++; break;
                }
            }
            catch (SemConexaoException)
            {
                throw;
            }
            catch (Exception ex)
            {
                erros.Add($"{Path.GetFileName(arquivo)}: {ex.Message}");
            }

            progresso?.Report((++feitos, grupos.Count));
        }

        return new ResumoEnvioPasta(criados, novas, identicos, duplicados, erros);
    }

    public async Task<EnvioTemplate> EnviarAlteracaoAsync(string codigo, bool forcar)
    {
        ExigirAdmin();

        var t = cache.LerTemplates().FirstOrDefault(x => x.Codigo == codigo && x.TemArquivoNoBanco)
                ?? throw new InvalidOperationException($"Template {codigo} não encontrado no acervo.");
        var caminho = CaminhoLocal(t.NomeArquivo!);
        cache.LerArquivosLocais().TryGetValue(codigo, out var m);

        var conteudo = LerArquivo(caminho);
        var r = await Online("Enviar alteração",
            () => repo.EnviarAsync(codigo, t.NomeArquivo!, conteudo, "edicao", versaoBase: m?.Versao ?? t.VersaoAtual, forcar: forcar));

        if (r.Resultado != ResultadoEnvio.Conflito)
            RegistrarManifesto(codigo, t.NomeArquivo!, r.Versao, HashArquivo.Sha256(conteudo), caminho);
        return r;
    }

    public Task<IReadOnlyList<TemplateVersao>> ListarVersoesAsync(string codigo)
        => Online("Ver versões", () => repo.ListarVersoesAsync(codigo));

    public async Task<EnvioTemplate> RestaurarAsync(string codigo, int versao)
    {
        ExigirAdmin();

        var t = cache.LerTemplates().FirstOrDefault(x => x.Codigo == codigo && x.TemArquivoNoBanco)
                ?? throw new InvalidOperationException($"Template {codigo} não encontrado no acervo.");

        var conteudo = await Online("Restaurar versão", () => repo.ConteudoDaVersaoAsync(codigo, versao))
                       ?? throw new InvalidOperationException($"A versão {versao} de {codigo} não está mais guardada.");

        var r = await Online("Restaurar versão", () => repo.EnviarAsync(codigo, t.NomeArquivo!, conteudo, "restauracao"));
        AtualizarCopiaLocal(codigo, t.NomeArquivo!, r.Versao, conteudo);
        return r;
    }

    public async Task<(int Copiados, int Faltando)> ExportarAsync(string pastaDestino)
    {
        Directory.CreateDirectory(pastaDestino);
        var copiados = 0;
        var faltando = 0;

        await Task.Run(() =>
        {
            foreach (var t in cache.LerTemplates().Where(x => x.TemArquivoNoBanco))
            {
                var origem = CaminhoLocal(t.NomeArquivo!);
                if (!File.Exists(origem)) { faltando++; continue; }
                File.Copy(origem, Path.Combine(pastaDestino, Path.GetFileName(t.NomeArquivo!)), overwrite: true);
                copiados++;
            }
        });

        return (copiados, faltando);
    }

    private void AtualizarCopiaLocal(string codigo, string nomeArquivo, int versao, byte[] conteudo)
    {
        try
        {
            GravarLocal(codigo, nomeArquivo, versao, HashArquivo.Sha256(conteudo), conteudo);
        }
        catch (IOException ex)
        {
            // Não impede o envio: a sincronização baixa depois.
            LogNaoGravou(logger, codigo, ex.Message);
        }
    }

    private static byte[] LerArquivo(string caminho)
    {
        using var stream = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static async Task<T> Online<T>(string acao, Func<Task<T>> operacao)
    {
        try
        {
            return await operacao();
        }
        catch (Exception ex) when (ISyncService.IsErroDeConexao(ex))
        {
            throw new SemConexaoException(acao, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Quantidade} template(s) baixado(s) para a pasta local")]
    private static partial void LogBaixados(ILogger logger, int quantidade);

    [LoggerMessage(Level = LogLevel.Error, Message = "Template {Codigo} v{Versao} veio com hash diferente; ignorado")]
    private static partial void LogHashInvalido(ILogger logger, string codigo, int versao);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Não foi possível gravar o template {Codigo}: {Motivo}")]
    private static partial void LogNaoGravou(ILogger logger, string codigo, string motivo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Alteração local descartada (perfil Impressão): {Arquivo} guardado em {Destino}")]
    private static partial void LogDescartado(ILogger logger, string arquivo, string destino);
}
