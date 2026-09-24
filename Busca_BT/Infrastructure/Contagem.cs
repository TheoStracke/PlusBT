using System.Globalization;

namespace Busca_BT.Infrastructure;

/// <summary>Textos de contagem em português: "1 etiqueta", "1.042 etiquetas".</summary>
public static class Contagem
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Texto(int quantidade, string singular, string plural)
        => $"{quantidade.ToString("N0", PtBr)} {(quantidade == 1 ? singular : plural)}";

    public static string Etiquetas(int n) => Texto(n, "etiqueta", "etiquetas");
    public static string Itens(int n) => Texto(n, "item", "itens");
    public static string Invoices(int n) => Texto(n, "invoice", "invoices");
}
