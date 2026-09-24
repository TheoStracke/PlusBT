namespace Busca_BT.Models
{
    public class ImportResult
    {
        public bool Success { get; init; }
        public int TotalRows { get; init; }
        public int ImportedRows { get; init; }
        public int SkippedRows { get; init; }
        public string? ErrorMessage { get; init; }

        /// <summary>Linhas gravadas na fila (vazio quando a importação falha).</summary>
        public IReadOnlyList<LabelRecord> Imported { get; init; } = [];

        /// <summary>Linhas da planilha que não entraram na fila, com o motivo.</summary>
        public IReadOnlyList<SkippedRow> Skipped { get; init; } = [];

        public static ImportResult Ok(int total, IReadOnlyList<LabelRecord> imported, IReadOnlyList<SkippedRow> skipped)
            => new()
            {
                Success = true,
                TotalRows = total,
                ImportedRows = imported.Count,
                SkippedRows = skipped.Count,
                Imported = imported,
                Skipped = skipped
            };

        public static ImportResult Fail(string errorMessage, IReadOnlyList<SkippedRow>? skipped = null)
            => new()
            {
                Success = false,
                ErrorMessage = errorMessage,
                SkippedRows = skipped?.Count ?? 0,
                Skipped = skipped ?? []
            };
    }

    /// <summary>Linha da planilha que não foi importada.</summary>
    /// <param name="Linha">Número da linha no Excel.</param>
    /// <param name="Codigo">Código do item (pode estar vazio).</param>
    /// <param name="Motivo">Explicação para o usuário.</param>
    public sealed record SkippedRow(int Linha, string Codigo, string Motivo)
    {
        public string Titulo => string.IsNullOrWhiteSpace(Codigo) ? $"Linha {Linha}" : $"Linha {Linha}  ·  {Codigo}";
    }
}
