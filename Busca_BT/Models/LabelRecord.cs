namespace Busca_BT.Models
{
    /// <summary>
    /// Representa uma etiqueta importada da planilha Excel.
    /// Mapeada 1:1 com a tabela [Labels] no SQLite.
    /// </summary>
    public class LabelRecord
    {
        public int Id { get; set; }
        public int Item { get; set; }
        public int BatchId { get; set; }
        public string Invoice { get; set; } = string.Empty;
        public string Codigo { get; set; } = string.Empty;
        public string DescricaoAnvisa { get; set; } = string.Empty;
        public int QtdInvoice { get; set; }
        public string Lote { get; set; } = string.Empty;
        public DateTime Validade { get; set; }
        public string RegistroAnvisa { get; set; } = string.Empty;
        public string Lpn { get; set; } = string.Empty;

        /// <summary>Caminho absoluto do arquivo .btw ou .pdf associado.</summary>
        public string? LabelFilePath { get; set; }

        public string? HashArquivo { get; init; }

        public LabelStatus Status { get; set; } = LabelStatus.Pendente;
        public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public bool HasFile => !string.IsNullOrWhiteSpace(LabelFilePath);
        public bool IsBtw => HasFile && LabelFilePath!.EndsWith(".btw", StringComparison.OrdinalIgnoreCase);
        public bool IsPdf => HasFile && LabelFilePath!.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public enum LabelStatus
    {
        Pendente = 0,
        Associada = 1,
        Impressa = 2,
        Cancelada = 9
    }
}