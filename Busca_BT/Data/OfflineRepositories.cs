using Busca_BT.Infrastructure;
using Busca_BT.Models;
using Busca_BT.Services;
using System.Text.Json;

namespace Busca_BT.Data;

/// <summary>Ação que precisa de internet tentada sem conexão. A mensagem já é para o operador.</summary>
public sealed class SemConexaoException(string acao, Exception? inner = null)
    : Exception($"Sem conexão com a internet. {acao} precisa de internet — tente de novo quando a conexão voltar.", inner);

/// <summary>
/// Fila para as telas: leitura sempre da cópia local (funciona offline); "abrir
/// etiqueta" grava na cópia e entra na fila de envio; as ações de administrador
/// (importar, limpar…) vão direto ao Supabase e exigem internet. Templates: ver
/// TemplateArquivoService.
/// </summary>
public sealed class OfflineLabelRepository(
    LabelRepository remoto,
    LocalCache cache,
    ISyncService sync,
    ISessaoOperador sessao) : ILabelRepository
{
    public Task<IReadOnlyList<LabelRecord>> GetAllAsync()
        => Task.Run(cache.LerItens);

    public Task<bool> MarcarComoAbertaAsync(int id, CancellationToken ct = default)
    {
        var quando = DateTime.UtcNow;
        cache.MarcarAberta(id, quando, sessao.Atual?.Nome);
        cache.Enfileirar(TiposPendentes.Abertura,
            JsonSerializer.Serialize(new AberturaPendente(id, quando, sessao.Atual?.Id)));
        sync.NotificarPendencia();
        return Task.FromResult(true);
    }

    // ── Precisam de internet ────────────────────────────────────────────────

    public Task<IEnumerable<ImportBatchRecord>> GetBatchesAsync(CancellationToken ct = default)
        => Online("Ver o histórico", () => remoto.GetBatchesAsync(ct), sincronizar: false);

    public Task DeleteBatchAsync(int batchId, CancellationToken ct = default)
        => Online("Excluir importação", async () => { await remoto.DeleteBatchAsync(batchId, ct); return 0; });

    public Task<int> ReplaceQueueAsync(string fileName, int total, int skipped, IReadOnlyList<LabelRecord> records)
        => Online("Importar planilha", () => remoto.ReplaceQueueAsync(fileName, total, skipped, records));

    public Task<int> ClearQueueAsync()
        => Online("Limpar a fila", remoto.ClearQueueAsync);

    /// <summary>
    /// Executa no Supabase; depois atualiza a cópia local para a tela já mostrar o resultado.
    /// Erro de rede vira SemConexaoException com mensagem para o operador.
    /// </summary>
    private async Task<T> Online<T>(string acao, Func<Task<T>> operacao, bool sincronizar = true)
    {
        T resultado;
        try
        {
            resultado = await operacao();
        }
        catch (Exception ex) when (ISyncService.IsErroDeConexao(ex))
        {
            throw new SemConexaoException(acao, ex);
        }

        if (sincronizar)
            await sync.SincronizarAgoraAsync();

        return resultado;
    }
}

/// <summary>
/// Operadores: a tela de entrada lê da cópia local (entra mesmo offline);
/// o cadastro (tela Operadores) vai direto ao Supabase e exige internet.
/// </summary>
public sealed class OfflineOperadorRepository(
    OperadorRepository remoto,
    LocalCache cache,
    ISyncService sync) : IOperadorRepository
{
    public async Task<IReadOnlyList<Operador>> ListarAsync(bool somenteAtivos)
    {
        if (somenteAtivos)
            return await Task.Run(() => cache.LerOperadores(somenteAtivos: true));

        try
        {
            return await remoto.ListarAsync(somenteAtivos: false);
        }
        catch (Exception ex) when (ISyncService.IsErroDeConexao(ex))
        {
            throw new SemConexaoException("Gerenciar operadores", ex);
        }
    }

    public async Task<Operador> CriarAsync(string nome, string perfil, string? pinHash)
        => await Online("Cadastrar operador", () => remoto.CriarAsync(nome, perfil, pinHash));

    public Task AtualizarAsync(Guid id, string nome, string perfil, bool ativo)
        => Online("Alterar operador", async () => { await remoto.AtualizarAsync(id, nome, perfil, ativo); return 0; });

    public Task DefinirPinAsync(Guid id, string? pinHash)
        => Online("Alterar o PIN", async () => { await remoto.DefinirPinAsync(id, pinHash); return 0; });

    private async Task<T> Online<T>(string acao, Func<Task<T>> operacao)
    {
        T resultado;
        try
        {
            resultado = await operacao();
        }
        catch (Exception ex) when (ISyncService.IsErroDeConexao(ex))
        {
            throw new SemConexaoException(acao, ex);
        }

        await sync.SincronizarAgoraAsync();
        return resultado;
    }
}
