using Busca_BT.Models;

namespace Busca_BT.Infrastructure;

/// <summary>
/// Ordem dos itens dentro de uma invoice: por LPN, do menor para o maior, ignorando a
/// coluna Item (1, 2, 3…) da planilha. Assim itens do mesmo LPN que vieram separados na
/// planilha ficam juntos. Usada na Home e no relatório, para os dois mostrarem a mesma ordem.
/// </summary>
public static class LpnOrdem
{
    /// <summary>LPN crescente (comparação natural: "CD9" vem antes de "CD10"); sem LPN no fim; empate pelo Item.</summary>
    public static List<LabelRecord> Ordenar(IEnumerable<LabelRecord> itens) => itens
        .OrderBy(l => string.IsNullOrWhiteSpace(l.Lpn))
        .ThenBy(l => l.Lpn.Trim(), ComparadorNatural.Instancia)
        .ThenBy(l => l.Item)
        .ToList();

    /// <summary>True quando o item começa um bloco de LPN diferente do item anterior.</summary>
    public static bool MudouLpn(LabelRecord? anterior, LabelRecord atual) =>
        anterior is null || !string.Equals(anterior.Lpn.Trim(), atual.Lpn.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Compara trechos de dígitos pelo valor numérico ("9" &lt; "10") e o resto sem diferenciar maiúsculas.</summary>
    private sealed class ComparadorNatural : IComparer<string>
    {
        public static readonly ComparadorNatural Instancia = new();

        public int Compare(string? x, string? y)
        {
            x ??= string.Empty;
            y ??= string.Empty;
            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    int fimX = i, fimY = j;
                    while (fimX < x.Length && char.IsDigit(x[fimX])) fimX++;
                    while (fimY < y.Length && char.IsDigit(y[fimY])) fimY++;

                    // Sem zeros à esquerda, o número com mais dígitos é o maior.
                    var numX = x[i..fimX].TrimStart('0');
                    var numY = y[j..fimY].TrimStart('0');
                    var cmp = numX.Length != numY.Length
                        ? numX.Length.CompareTo(numY.Length)
                        : string.CompareOrdinal(numX, numY);
                    if (cmp != 0) return cmp;

                    i = fimX;
                    j = fimY;
                }
                else
                {
                    var cmp = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                    if (cmp != 0) return cmp;
                    i++;
                    j++;
                }
            }
            return (x.Length - i).CompareTo(y.Length - j);
        }
    }
}
