namespace Busca_BT.Infrastructure;

/// <summary>
/// Calcula quantas folhas de espelho (página enviada à Anvisa) são necessárias.
/// Cada ITEM/linha da planilha vira 1 etiqueta colada no espelho. A primeira
/// folha comporta 3 etiquetas; cada folha seguinte comporta 4.
/// </summary>
public static class EspelhoCalculator
{
    public const int EtiquetasPrimeiraFolha = 3;
    public const int EtiquetasFolhasSeguintes = 4;

    public static int CalcularFolhas(int totalEtiquetas)
    {
        if (totalEtiquetas <= 0)
            return 0;

        if (totalEtiquetas <= EtiquetasPrimeiraFolha)
            return 1;

        var restante = totalEtiquetas - EtiquetasPrimeiraFolha;
        return 1 + (int)Math.Ceiling(restante / (double)EtiquetasFolhasSeguintes);
    }
}
