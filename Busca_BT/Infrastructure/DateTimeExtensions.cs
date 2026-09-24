namespace Busca_BT.Infrastructure;

/// <summary>
/// O banco grava todas as datas em UTC (SYSUTCDATETIME / DateTime.UtcNow) e o Dapper
/// as devolve com Kind=Unspecified. Use isto para exibir no horário local da máquina.
/// </summary>
public static class DateTimeExtensions
{
    public static DateTime UtcToLocal(this DateTime utc)
        => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
}
