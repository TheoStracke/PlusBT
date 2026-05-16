namespace Busca_BT.Models;

public sealed record ImportBatchRecord(
    int Id,
    string FileName,
    DateTime ImportedAt,
    int TotalRows,
    int ImportedRows,
    int SkippedRows)
{
    public string Resumo =>
        $"{FileName}  •  {ImportedAt:dd/MM/yyyy HH:mm}  •  {ImportedRows} registros";
}