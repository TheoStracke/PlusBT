namespace Busca_BT.Infrastructure;

/// <summary>
/// Calcula quantas folhas de espelho (página enviada à Anvisa) são necessárias.
/// Cada invoice distinto vira 1 etiqueta colada no espelho. A primeira folha
/// comporta 3 invoices; cada folha seguinte comporta 4.
/// </summary>
public static class EspelhoCalculator
{
    public const int InvoicesPrimeiraFolha = 3;
    public const int InvoicesFolhasSeguintes = 4;

    public static int CalcularFolhas(int totalInvoicesDistintos)
    {
        if (totalInvoicesDistintos <= 0)
            return 0;

        if (totalInvoicesDistintos <= InvoicesPrimeiraFolha)
            return 1;

        var restante = totalInvoicesDistintos - InvoicesPrimeiraFolha;
        return 1 + (int)Math.Ceiling(restante / (double)InvoicesFolhasSeguintes);
    }
}
