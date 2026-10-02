namespace Busca_BT.Models;

/// <summary>Um template do acervo, como está no banco (sem o conteúdo do arquivo).</summary>
public sealed class TemplateInfo
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;

    /// <summary>Nome do arquivo .btw (ex.: "C76421.btw"); é o nome usado na cópia local.</summary>
    public string? NomeArquivo { get; set; }

    /// <summary>0 = o banco ainda não tem o arquivo deste template.</summary>
    public int VersaoAtual { get; set; }

    public string? HashAtual { get; set; }
    public DateTime? AtualizadoEm { get; set; }
    public string? AtualizadoPor { get; set; }

    public bool TemArquivoNoBanco => VersaoAtual > 0 && !string.IsNullOrEmpty(NomeArquivo);
}

/// <summary>Uma versão guardada de um template (para a lista de versões e "Restaurar").</summary>
public sealed class TemplateVersao
{
    public int Versao { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public int Tamanho { get; set; }
    public string Origem { get; set; } = string.Empty;
    public DateTime EnviadoEm { get; set; }
    public string? EnviadoPor { get; set; }

    public string OrigemTexto => Origem switch
    {
        "upload" => "Envio de arquivo",
        "importacao_pasta" => "Importação de pasta",
        "edicao" => "Edição no BarTender",
        "restauracao" => "Restauração",
        _ => Origem
    };

    public string Resumo =>
        $"v{Versao} · {Infrastructure.DateTimeExtensions.UtcToLocal(EnviadoEm):dd/MM/yyyy HH:mm}" +
        $"{(EnviadoPor is null ? "" : $" · {EnviadoPor}")} · {OrigemTexto} · {Tamanho / 1024.0:0} KB";
}

/// <summary>Resultado do envio de um arquivo ao acervo.</summary>
public enum ResultadoEnvio
{
    /// <summary>Template novo (versão 1).</summary>
    Criado,
    /// <summary>Nova versão de um template que já existia.</summary>
    NovaVersao,
    /// <summary>Arquivo idêntico à versão atual (mesmo hash): nada foi gravado.</summary>
    Identico,
    /// <summary>Outra pessoa enviou uma versão depois da que foi editada aqui: nada foi gravado.</summary>
    Conflito
}

public sealed record EnvioTemplate(
    string Codigo, ResultadoEnvio Resultado, int Versao, string? ConflitoPor = null, DateTime? ConflitoEm = null);
