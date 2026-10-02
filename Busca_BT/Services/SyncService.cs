using Busca_BT.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Busca_BT.Services;

public enum EstadoConexao { Desconhecido, Online, Offline, Sincronizando }

/// <summary>Tipos de ação guardados na fila de envio.</summary>
public static class TiposPendentes
{
    public const string Evento = "evento";
    public const string Abertura = "abertura";
}

/// <summary>Payload de "item aberto" na fila de envio.</summary>
public sealed record AberturaPendente(int ItemId, DateTime QuandoUtc, Guid? OperadorId);

/// <summary>Payload de evento na fila de envio (o id vem do PC: reenviar não duplica).</summary>
public sealed record EventoPendente(
    Guid Id, DateTime OcorridoEmUtc, Guid? OperadorId, string Pc, string Acao,
    string? Invoice, string? Codigo, string? DetalhesJson);

public interface ISyncService
{
    EstadoConexao Estado { get; }
    DateTime? UltimaSincronizacao { get; }
    int Pendentes { get; }

    /// <summary>Estado, última sincronização ou pendentes mudaram (pode vir de outra thread).</summary>
    event Action? StatusMudou;

    /// <summary>A cópia local recebeu dados novos do Supabase (pode vir de outra thread).</summary>
    event Action? DadosAtualizados;

    /// <summary>Envia as pendências e, se não sobrar nenhuma, baixa a cópia atualizada.</summary>
    Task<bool> SincronizarAgoraAsync(CancellationToken ct = default);

    /// <summary>Liga a sincronização automática em segundo plano.</summary>
    void Iniciar();

    /// <summary>Uma ação nova entrou na fila de envio: tenta enviar já.</summary>
    void NotificarPendencia();

    /// <summary>Informa que o banco já foi inicializado (migrações aplicadas) nesta execução.</summary>
    void MarcarBancoInicializado();

    /// <summary>True para erros de rede/conexão (cair para o modo offline), false para erros de dados.</summary>
    static bool IsErroDeConexao(Exception ex) => ex switch
    {
        NpgsqlException { IsTransient: true } => true,
        NpgsqlException { InnerException: SocketException or IOException or TimeoutException } => true,
        SocketException or IOException or TimeoutException => true,
        InvalidOperationException ioe when ioe.Message.Contains("não configurada") => true,
        _ when ex.InnerException is not null => IsErroDeConexao(ex.InnerException),
        _ => false
    };
}

public sealed partial class SyncService(
    LocalCache cache,
    IDbConnectionFactory factory,
    LabelRepository remoto,
    OperadorRepository operadoresRemoto,
    DatabaseInitializer initializer,
    ILogger<SyncService> logger) : ISyncService
{
    // Uma ação que falha por erro de dados (não de rede) é tentada no máximo isso.
    public const int MaxTentativas = 5;

    private static readonly TimeSpan IntervaloOnline = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IntervaloOffline = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly SemaphoreSlim _acordar = new(0, 1);
    private bool _bancoInicializado;
    private string? _ultimoHash;
    private CancellationTokenSource? _cts;

    public EstadoConexao Estado { get; private set; } = EstadoConexao.Desconhecido;
    public DateTime? UltimaSincronizacao { get; private set; } = cache.UltimaSincronizacao;
    public int Pendentes { get; private set; } = cache.ContarPendentes(MaxTentativas);

    public event Action? StatusMudou;
    public event Action? DadosAtualizados;

    public void MarcarBancoInicializado() => _bancoInicializado = true;

    public void Iniciar()
    {
        if (_cts is not null)
            return;

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void NotificarPendencia()
    {
        AtualizarPendentes();
        if (_acordar.CurrentCount == 0)
            _acordar.Release();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await SincronizarAgoraAsync(ct);

            var espera = Estado == EstadoConexao.Online ? IntervaloOnline : IntervaloOffline;
            try { await _acordar.WaitAsync(espera, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    public async Task<bool> SincronizarAgoraAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            MudarEstado(EstadoConexao.Sincronizando);

            if (!_bancoInicializado)
            {
                await initializer.InitializeAsync(ct);
                _bancoInicializado = true;
            }

            var restantes = await EnviarPendentesAsync(ct);

            // Só baixa a cópia quando não sobrou ação pendente: senão a cópia do Supabase
            // ainda não tem o que foi feito aqui e apagaria a marcação local.
            if (restantes == 0)
                await BaixarCopiaAsync(ct);

            UltimaSincronizacao = DateTime.UtcNow;
            cache.UltimaSincronizacao = UltimaSincronizacao;
            MudarEstado(EstadoConexao.Online);
            return true;
        }
        catch (Exception ex) when (ISyncService.IsErroDeConexao(ex) || ex is OperationCanceledException)
        {
            LogOffline(logger, ex.Message);
            MudarEstado(EstadoConexao.Offline);
            return false;
        }
        catch (Exception ex)
        {
            LogFalhaSync(logger, ex);
            MudarEstado(EstadoConexao.Offline);
            return false;
        }
        finally
        {
            AtualizarPendentes();
            _lock.Release();
        }
    }

    /// <summary>Envia as ações na ordem em que foram feitas. Retorna quantas ainda faltam.</summary>
    private async Task<int> EnviarPendentesAsync(CancellationToken ct)
    {
        var pendentes = cache.LerPendentes(MaxTentativas);
        if (pendentes.Count == 0)
            return 0;

        await using var conn = await factory.OpenAsync(ct);

        foreach (var p in pendentes)
        {
            try
            {
                switch (p.Tipo)
                {
                    case TiposPendentes.Abertura:
                        var a = JsonSerializer.Deserialize<AberturaPendente>(p.Payload)!;
                        await conn.ExecuteAsync("""
                            update plusbt.itens
                            set    aberta_em = @Quando, aberta_por = @OperadorId, atualizado_em = now()
                            where  id = @ItemId and aberta_em is null;
                            """, new { Quando = DateTime.SpecifyKind(a.QuandoUtc, DateTimeKind.Utc), a.OperadorId, a.ItemId });
                        break;

                    case TiposPendentes.Evento:
                        var e = JsonSerializer.Deserialize<EventoPendente>(p.Payload)!;
                        await conn.ExecuteAsync("""
                            insert into plusbt.eventos (id, ocorrido_em, operador_id, pc, acao, invoice, codigo, detalhes)
                            values (@Id, @Quando, @OperadorId, @Pc, @Acao, @Invoice, @Codigo, cast(@DetalhesJson as jsonb))
                            on conflict (id) do nothing;
                            """, new
                        {
                            e.Id,
                            Quando = DateTime.SpecifyKind(e.OcorridoEmUtc, DateTimeKind.Utc),
                            e.OperadorId,
                            e.Pc,
                            e.Acao,
                            e.Invoice,
                            e.Codigo,
                            e.DetalhesJson
                        });
                        break;
                }

                cache.RemoverPendente(p.Seq);
            }
            catch (Exception ex) when (!ISyncService.IsErroDeConexao(ex))
            {
                // Erro de dados (não de rede): tenta de novo nas próximas, até desistir.
                LogPendenteFalhou(logger, p.Tipo, p.Seq, ex);
                cache.RegistrarFalha(p.Seq, ex.Message);
            }
        }

        return cache.ContarPendentes(MaxTentativas);
    }

    private async Task BaixarCopiaAsync(CancellationToken ct)
    {
        var itens = await remoto.GetAllAsync();
        var templates = (await remoto.GetAllTemplatesMasterAsync()).ToList();
        var operadores = await operadoresRemoto.ListarAsync(somenteAtivos: false);
        ct.ThrowIfCancellationRequested();

        // Só regrava e avisa as telas quando algo mudou de fato.
        var hash = Hash(itens, templates, operadores);
        if (hash == _ultimoHash)
            return;

        cache.SubstituirTudo(itens, templates, operadores);
        var primeiraCarga = _ultimoHash is null;
        _ultimoHash = hash;

        if (!primeiraCarga)
            LogDadosNovos(logger, itens.Count);
        DadosAtualizados?.Invoke();
    }

    private static string Hash(object itens, object templates, object operadores)
    {
        var json = JsonSerializer.Serialize(new { itens, templates, operadores });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private void MudarEstado(EstadoConexao estado)
    {
        if (Estado == estado)
            return;
        Estado = estado;
        StatusMudou?.Invoke();
    }

    private void AtualizarPendentes()
    {
        var n = cache.ContarPendentes(MaxTentativas);
        if (n == Pendentes)
            return;
        Pendentes = n;
        StatusMudou?.Invoke();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sem conexão com o Supabase (modo offline): {Motivo}")]
    private static partial void LogOffline(ILogger logger, string motivo);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha inesperada na sincronização")]
    private static partial void LogFalhaSync(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Ação pendente '{Tipo}' #{Seq} recusada pelo banco")]
    private static partial void LogPendenteFalhou(ILogger logger, string tipo, long seq, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cópia local atualizada: {Itens} itens na fila")]
    private static partial void LogDadosNovos(ILogger logger, int itens);
}
