using Busca_BT.Data;
using Busca_BT.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Busca_BT.Services;

/// <summary>Ações registradas na tabela plusbt.eventos.</summary>
public static class Acoes
{
    public const string Entrou = "entrou";
    public const string Saiu = "saiu";
    public const string Importou = "importou";
    public const string Abriu = "abriu";
    public const string Ciencia = "ciencia";
    public const string LimpouFila = "limpou_fila";
    public const string ExcluiuImportacao = "excluiu_importacao";
    public const string TemplateAlterado = "template_alterado";
    public const string OperadorAlterado = "operador_alterado";
}

public interface IEventoService
{
    /// <summary>
    /// Registra quem fez o quê (operador da sessão + este PC). O evento entra na fila de
    /// envio local e sobe para o Supabase quando houver conexão (funciona offline).
    /// Nunca lança: uma falha no registro não pode impedir o trabalho do operador.
    /// </summary>
    Task RegistrarAsync(string acao, string? invoice = null, string? codigo = null, object? detalhes = null);
}

public sealed partial class EventoService(
    LocalCache cache,
    ISyncService sync,
    ISessaoOperador sessao,
    ILogger<EventoService> logger) : IEventoService
{
    public Task RegistrarAsync(string acao, string? invoice = null, string? codigo = null, object? detalhes = null)
    {
        try
        {
            var evento = new EventoPendente(
                Guid.NewGuid(), DateTime.UtcNow, sessao.Atual?.Id, sessao.Pc, acao, invoice, codigo,
                detalhes is null ? null : JsonSerializer.Serialize(detalhes));

            cache.Enfileirar(TiposPendentes.Evento, JsonSerializer.Serialize(evento));
            sync.NotificarPendencia();
        }
        catch (Exception ex)
        {
            LogFalha(logger, acao, ex);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao registrar o evento '{Acao}'")]
    private static partial void LogFalha(ILogger logger, string acao, Exception ex);
}
