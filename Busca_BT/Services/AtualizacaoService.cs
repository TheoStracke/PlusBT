using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace Busca_BT.Services;

public enum EstadoAtualizacao { SemAtualizacao, Baixando, Pronta }

public interface IAtualizacaoService
{
    /// <summary>Versão em execução ("dev" quando roda pelo Visual Studio, sem instalar).</summary>
    string VersaoAtual { get; }

    /// <summary>Versão nova já baixada e pronta para instalar, se houver.</summary>
    string? VersaoNova { get; }

    EstadoAtualizacao Estado { get; }

    /// <summary>Estado mudou (pode vir de outra thread).</summary>
    event Action? StatusMudou;

    /// <summary>Procura no GitHub Releases e, se houver versão nova, baixa em segundo plano.</summary>
    Task VerificarAsync(CancellationToken ct = default);

    /// <summary>Liga a verificação automática (agora e a cada poucas horas).</summary>
    void Iniciar();

    /// <summary>Fecha o app, instala a versão baixada e abre de novo.</summary>
    void ReiniciarEAtualizar();
}

/// <summary>
/// Atualização pela internet com o Velopack, a partir das releases públicas do GitHub.
/// A versão baixada é instalada ao clicar em "Reiniciar e atualizar" ou, se ninguém
/// clicar, na próxima vez que o app for aberto (o PC de impressão não é interrompido).
/// </summary>
public sealed partial class AtualizacaoService : IAtualizacaoService
{
    public const string RepositorioUrl = "https://github.com/TheoStracke/PlusBT";

    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(4);

    private readonly UpdateManager _manager;
    private readonly ILogger<AtualizacaoService> _logger;
    private readonly SemaphoreSlim _emAndamento = new(1, 1);
    private UpdateInfo? _pronta;
    private Timer? _timer;

    public AtualizacaoService(ILogger<AtualizacaoService> logger)
    {
        _logger = logger;
        // PLUSBT_ATUALIZACAO_ORIGEM (pasta ou URL com os pacotes do vpk) troca o GitHub
        // por outra origem: serve para testar uma versão antes de publicar.
        var origem = Environment.GetEnvironmentVariable("PLUSBT_ATUALIZACAO_ORIGEM");
        _manager = string.IsNullOrWhiteSpace(origem)
            ? new UpdateManager(new GithubSource(RepositorioUrl, accessToken: null, prerelease: false))
            : new UpdateManager(origem);
    }

    public string VersaoAtual => _manager.CurrentVersion?.ToString() ?? "dev";
    public string? VersaoNova => _pronta?.TargetFullRelease.Version.ToString();
    public EstadoAtualizacao Estado { get; private set; }

    public event Action? StatusMudou;

    public void Iniciar()
    {
        // Rodando pelo Visual Studio (sem instalar pelo Setup) não há o que atualizar.
        if (!_manager.IsInstalled)
            return;

        _timer ??= new Timer(_ => _ = VerificarAsync(), null, TimeSpan.FromSeconds(10), Intervalo);
    }

    public async Task VerificarAsync(CancellationToken ct = default)
    {
        if (!_manager.IsInstalled || _pronta is not null)
            return;
        if (!await _emAndamento.WaitAsync(0, ct))
            return;

        try
        {
            var info = await _manager.CheckForUpdatesAsync();
            if (info is null)
                return;

            MudarEstado(EstadoAtualizacao.Baixando);
            await _manager.DownloadUpdatesAsync(info, cancelToken: ct);
            _pronta = info;
            MudarEstado(EstadoAtualizacao.Pronta);
            LogBaixada(_logger, info.TargetFullRelease.Version.ToString());
        }
        catch (Exception ex)
        {
            // Sem internet ou GitHub fora do ar: tenta de novo no próximo intervalo.
            MudarEstado(_pronta is null ? EstadoAtualizacao.SemAtualizacao : EstadoAtualizacao.Pronta);
            LogFalha(_logger, ex);
        }
        finally
        {
            _emAndamento.Release();
        }
    }

    public void ReiniciarEAtualizar()
    {
        if (_pronta is not null)
            _manager.ApplyUpdatesAndRestart(_pronta.TargetFullRelease);
    }

    private void MudarEstado(EstadoAtualizacao estado)
    {
        if (Estado == estado)
            return;
        Estado = estado;
        StatusMudou?.Invoke();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Atualização {Versao} baixada; será instalada ao reiniciar.")]
    private static partial void LogBaixada(ILogger logger, string versao);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Não foi possível verificar ou baixar atualizações.")]
    private static partial void LogFalha(ILogger logger, Exception ex);
}
