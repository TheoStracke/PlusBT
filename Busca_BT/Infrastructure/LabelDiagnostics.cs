using Busca_BT.Models;
using System.Globalization;
using System.IO;
using System.Text;

namespace Busca_BT.Infrastructure;

/// <summary>
/// Vincula cada etiqueta da fila ao seu template (.btw) e monta a lista de pendências
/// que a Home mostra na linha. O vínculo é tolerante a diferenças que não mudam o
/// código: maiúsculas/minúsculas, espaços, caracteres invisíveis vindos do Excel,
/// zeros à esquerda em códigos numéricos e arquivos com sufixo ("C76421 - Free T4.btw").
/// </summary>
public static class LabelDiagnostics
{
    public static void Aplicar(IReadOnlyList<LabelRecord> labels, IEnumerable<LabelRecord> templates, DateTime hoje)
    {
        var porChave = new Dictionary<string, LabelRecord>();
        // Prefixo do nome do arquivo até o primeiro separador ("C76421 - Free T4" → "C76421").
        var porPrefixo = new Dictionary<string, List<LabelRecord>>();
        foreach (var t in templates)
        {
            porChave.TryAdd(Chave(t.Codigo), t);

            var prefixo = Chave(Prefixo(t.Codigo));
            if (prefixo.Length == 0)
                continue;
            if (!porPrefixo.TryGetValue(prefixo, out var lista))
                porPrefixo[prefixo] = lista = [];
            lista.Add(t);
        }

        // File.Exists em pasta de rede é lento: consulta cada caminho uma vez só.
        var existe = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        bool ArquivoExiste(string path)
        {
            if (!existe.TryGetValue(path, out var ok))
                existe[path] = ok = File.Exists(path);
            return ok;
        }

        foreach (var label in labels)
        {
            var pendencias = new List<string>();

            if (!string.IsNullOrWhiteSpace(label.Avisos))
                pendencias.AddRange(label.Avisos.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            if (label.Validade is DateTime validade && validade.Date < hoje.Date)
                pendencias.Add($"Produto vencido (validade {validade:dd/MM/yyyy}).");

            label.LabelFilePath = null;
            var chave = Chave(label.Codigo);

            if (chave.Length > 0)
            {
                var template = Encontrar(chave, porChave, porPrefixo);
                var path = template?.LabelFilePath;

                if (template is null)
                    pendencias.Add($"Sem template: nenhum arquivo .btw cadastrado para o código '{label.Codigo}'.");
                else if (string.IsNullOrWhiteSpace(path))
                    pendencias.Add($"Template '{template.Codigo}' cadastrado sem arquivo.");
                else if (!ArquivoExiste(path))
                    pendencias.Add($"Arquivo do template não encontrado: {path}");
                else
                    label.LabelFilePath = path;
            }

            label.Pendencias = pendencias;
        }
    }

    private static LabelRecord? Encontrar(
        string chave, Dictionary<string, LabelRecord> porChave, Dictionary<string, List<LabelRecord>> porPrefixo)
    {
        if (porChave.TryGetValue(chave, out var exato))
            return exato;

        // Arquivo com sufixo ("C76421 - Free T4", "C76421_v2"): só aceita se houver um único candidato.
        return porPrefixo.TryGetValue(chave, out var candidatos) && candidatos.Count == 1
            ? candidatos[0]
            : null;
    }

    private static string Prefixo(string nome)
    {
        var texto = nome.Trim();
        var fim = 0;
        while (fim < texto.Length && char.IsLetterOrDigit(texto[fim]))
            fim++;
        return texto[..fim];
    }

    /// <summary>
    /// Chave de comparação de código: sem espaços nem caracteres invisíveis, em maiúsculas
    /// e, quando só tem dígitos, sem zeros à esquerda (o Excel some com eles).
    /// </summary>
    public static string Chave(string? codigo)
    {
        if (string.IsNullOrEmpty(codigo))
            return string.Empty;

        var sb = new StringBuilder(codigo.Length);
        foreach (var ch in codigo.Normalize(NormalizationForm.FormC))
        {
            if (char.IsWhiteSpace(ch) || CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.Format)
                continue;
            sb.Append(char.ToUpperInvariant(ch));
        }

        var chave = sb.ToString();
        if (chave.Length > 1 && chave.All(char.IsAsciiDigit))
            chave = chave.TrimStart('0') is { Length: > 0 } semZeros ? semZeros : "0";

        return chave;
    }
}
