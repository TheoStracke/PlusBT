namespace Busca_BT.Models
{
    public class ImportResult
    {
        public bool Success { get; init; }
        public int TotalRows { get; init; }
        public int ImportedRows { get; init; }
        public int SkippedRows { get; init; }
        public int AssociatedFiles { get; init; } // NOVO: Contador de arquivos associados
        public string? ErrorMessage { get; init; }
        public IReadOnlyList<string> RowErrors { get; init; } = [];

        public static ImportResult Ok(int total, int imported, int skipped,
                                      IReadOnlyList<string>? rowErrors = null, int associatedFiles = 0)
            => new()
            {
                Success = true,
                TotalRows = total,
                ImportedRows = imported,
                SkippedRows = skipped,
                AssociatedFiles = associatedFiles,
                RowErrors = rowErrors ?? []
            };

        public static ImportResult Fail(string errorMessage)
            => new() { Success = false, ErrorMessage = errorMessage };
    }

    public class LabelFilter
    {
        public string? Lote { get; init; }
        public string? Lpn { get; init; }
        public string? Invoice { get; init; }
        public LabelStatus? Status { get; init; }

        public bool HasCriteria =>
            !string.IsNullOrWhiteSpace(Lote) ||
            !string.IsNullOrWhiteSpace(Lpn) ||
            !string.IsNullOrWhiteSpace(Invoice) ||
            Status.HasValue;
    }

    public class BartenderPrintOptions
    {
        public int Copies { get; init; } = 1;
        public string? PrinterName { get; init; }

        /// <summary>
        /// Variáveis substituídas na etiqueta via /NSS da CLI do Bartender.
        /// Chave = nome do campo na etiqueta | Valor = conteúdo a preencher.
        /// </summary>
        public Dictionary<string, string> NamedSubStrings { get; init; } = [];
    }
}