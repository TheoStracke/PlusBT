using Busca_BT.Data;
using Busca_BT.Infrastructure;
using Dapper;
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
    /// Registra quem fez o quê (operador da sessão + este PC). Nunca lança: uma falha
    /// no registro não pode impedir o trabalho do operador; ela só vai para o log.
    /// </summary>
    Task RegistrarAsync(string acao, string? invoice = null, string? codigo = null, object? detalhes = null);
}

public sealed partial class EventoService(
    IDbConnectionFactory factory,
    ISessaoOperador sessao,
    ILogger<EventoService> logger) : IEventoService
{
    public async Task RegistrarAsync(string acao, string? invoice = null, string? codigo = null, object? detalhes = null)
    {
        try
        {
            await using var conn = await factory.OpenAsync();
            await conn.ExecuteAsync("""
                insert into plusbt.eventos (id, operador_id, pc, acao, invoice, codigo, detalhes)
                values (@Id, @OperadorId, @Pc, @Acao, @Invoice, @Codigo, cast(@Detalhes as jsonb));
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    OperadorId = sessao.Atual?.Id,
                    sessao.Pc,
                    Acao = acao,
                    Invoice = invoice,
                    Codigo = codigo,
                    Detalhes = detalhes is null ? null : JsonSerializer.Serialize(detalhes)
                });
        }
        catch (Exception ex)
        {
            LogFalha(logger, acao, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao registrar o evento '{Acao}'")]
    private static partial void LogFalha(ILogger logger, string acao, Exception ex);
}
