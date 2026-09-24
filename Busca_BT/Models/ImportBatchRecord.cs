using Busca_BT.Infrastructure;

namespace Busca_BT.Models;

public sealed record ImportBatchRecord(
    int Id,
    string FileName,
    DateTime ImportedAt, // UTC, como gravado no banco
    int TotalRows,
    int ImportedRows,
    int SkippedRows)
{
    public DateTime ImportedAtLocal => ImportedAt.UtcToLocal();

    public string Resumo =>
        $"{FileName}  •  {ImportedAtLocal:dd/MM/yyyy HH:mm}  •  {ImportedRows} registros";
}
