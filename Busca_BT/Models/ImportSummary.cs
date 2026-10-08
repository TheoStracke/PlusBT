using Busca_BT.Infrastructure;
using System.IO;

namespace Busca_BT.Models;

/// <summary>Grupo de etiquetas importadas de uma mesma invoice (exibido no modal de resumo).</summary>
public sealed record ImportedInvoiceGroup(string Invoice, int Quantidade, int Etiquetas, int Pacotes)
{
    public string QuantidadeTexto =>
        $"{Contagem.Itens(Quantidade)}  ·  {Contagem.Etiquetas(Etiquetas)}  ·  {Contagem.Pacotes(Pacotes)}";
}

/// <summary>Item da fila com pendência, exibido no modal de resumo.</summary>
public sealed record PendenciaResumo(string Titulo, string Motivo);

/// <summary>Dados do modal exibido ao final de uma importação de planilha.</summary>
public sealed class ImportSummary
{
    public bool Success { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }

    public IReadOnlyList<ImportedInvoiceGroup> Importados { get; init; } = [];
    public IReadOnlyList<SkippedRow> NaoImportados { get; init; } = [];

    /// <summary>Itens que entraram na fila, mas com pendência (sem template, data inválida…).</summary>
    public IReadOnlyList<PendenciaResumo> ComPendencia { get; init; } = [];
    public bool HasPendencias => ComPendencia.Count > 0;
    public string PendenciasTexto => ComPendencia.Count == 1
        ? "1 item com pendência — entrou na fila, mas precisa de atenção"
        : $"{ComPendencia.Count} itens com pendência — entraram na fila, mas precisam de atenção";

    public int TotalImportados => Importados.Sum(g => g.Quantidade);

    public bool Failed => !Success;
    public bool HasImportados => TotalImportados > 0;
    public bool HasNaoImportados => NaoImportados.Count > 0;

    public string Titulo => Success ? "Planilha importada" : "Planilha não importada";

    public string ImportadosTexto =>
        $"{Contagem.Texto(TotalImportados, "item importado", "itens importados")}  ·  " +
        $"{Contagem.Etiquetas(Importados.Sum(g => g.Etiquetas))}  ·  {Contagem.Pacotes(TotalPacotes)}";

    /// <summary>LPNs diferentes entre todos os itens importados.</summary>
    public int TotalPacotes { get; init; }

    public string NaoImportadosTexto => NaoImportados.Count == 1
        ? "1 linha não importada"
        : $"{NaoImportados.Count} linhas não importadas";

    // ── Relatórios .xlsx por invoice (gerados automaticamente na importação) ──

    /// <summary>Pasta onde os relatórios foram salvos (null = não gerados).</summary>
    public string? RelatorioPasta { get; init; }
    public int RelatorioQuantidade { get; init; }

    /// <summary>Erro ao gerar os relatórios (a importação em si deu certo).</summary>
    public string? RelatorioErro { get; init; }

    public bool HasRelatorio => RelatorioPasta is not null;
    public bool HasRelatorioErro => RelatorioErro is not null;

    public string RelatorioTexto => RelatorioQuantidade == 1
        ? "1 relatório salvo em Downloads"
        : $"{RelatorioQuantidade} relatórios salvos em Downloads";

    public static ImportSummary From(
        string filePath, ImportResult result,
        string? relatorioPasta = null, int relatorioQuantidade = 0, string? relatorioErro = null,
        IReadOnlyList<LabelRecord>? comPendencia = null) => new()
    {
        ComPendencia = (comPendencia ?? [])
            .Select(l => new PendenciaResumo($"Invoice {l.Invoice}  ·  Item {l.Item}  ·  {l.Codigo}", l.PendenciasTexto))
            .ToList(),
        Success = result.Success,
        RelatorioPasta = relatorioPasta,
        RelatorioQuantidade = relatorioQuantidade,
        RelatorioErro = relatorioErro,
        FileName = Path.GetFileName(filePath),
        ErrorMessage = result.ErrorMessage,
        Importados = result.Imported
            .GroupBy(l => l.Invoice)
            .Select(g => new ImportedInvoiceGroup(g.Key, g.Count(), g.Sum(l => l.QtdInvoice), LpnOrdem.ContarPacotes(g)))
            .ToList(),
        TotalPacotes = LpnOrdem.ContarPacotes(result.Imported),
        NaoImportados = result.Skipped
    };

    public static ImportSummary FromException(string filePath, Exception ex) => new()
    {
        Success = false,
        FileName = Path.GetFileName(filePath),
        ErrorMessage = $"{ex.Message} A fila atual foi mantida."
    };
}
