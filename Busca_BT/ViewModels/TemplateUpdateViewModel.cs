using Busca_BT.Data;
using Busca_BT.Infrastructure;
using Busca_BT.Models;
using Busca_BT.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;

namespace Busca_BT.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// Linha da grid
// ─────────────────────────────────────────────────────────────────────────────

public sealed class TemplateItem(TemplateLocal local)
{
    public TemplateLocal Local { get; } = local;
    public string Codigo => Local.Info.Codigo;
    public string NomeArquivo => Local.Info.NomeArquivo ?? "—";
    public StatusArquivo Status => Local.Status;

    public string VersaoTexto => Local.Info.VersaoAtual > 0 ? $"v{Local.Info.VersaoAtual}" : "—";

    public string AtualizadoTexto =>
        (Local.Info.AtualizadoEm?.UtcToLocal().ToString("dd/MM/yyyy HH:mm") ?? "—") +
        (Local.Info.AtualizadoPor is { } por ? $" · {por}" : "");

    public bool PodeAbrir => Local.CaminhoLocal is { } c && File.Exists(c);
    public bool TemAlteracaoLocal => Status is StatusArquivo.AlteradoAqui or StatusArquivo.Conflito;

    public string StatusTexto => Status switch
    {
        StatusArquivo.Sincronizado => "Sincronizado",
        StatusArquivo.Desatualizado => "Versão nova a baixar",
        StatusArquivo.NaoBaixado => "Não baixado",
        StatusArquivo.AlteradoAqui => "Alterado neste PC",
        StatusArquivo.Conflito => "Alterado aqui e no banco",
        _ => "Sem arquivo no banco"
    };

    public string StatusCor => Status switch
    {
        StatusArquivo.Sincronizado => "#16A34A",
        StatusArquivo.AlteradoAqui => "#D97706",
        StatusArquivo.Conflito => "#DC2626",
        _ => "#6B7280"
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// ViewModel
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Acervo de templates (só administrador): adicionar arquivos/pasta, enviar alteração
/// feita no BarTender, versões guardadas e restaurar, exportar o acervo.
/// </summary>
public sealed partial class TemplateUpdateViewModel(
    ITemplateArquivos arquivos,
    ISyncService sync,
    IEventoService eventos,
    IDialogService dialog,
    IPreferenciasStore preferencias,
    ILogger<TemplateUpdateViewModel> logger) : ObservableObject
{
    private List<TemplateItem> _todos = [];

    [ObservableProperty]
    private ObservableCollection<TemplateItem> _templates = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AbrirCommand), nameof(EnviarAlteracaoCommand), nameof(EnviarNovaVersaoCommand))]
    private TemplateItem? _selected;

    public ObservableCollection<TemplateVersao> Versoes { get; } = [];

    [ObservableProperty]
    private string? _versoesMensagem;

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    /// <summary>Mostra só os templates alterados neste PC (para enviar).</summary>
    [ObservableProperty]
    private bool _somenteAlterados;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _resumo = string.Empty;

    public bool ModoPastaLocal => preferencias.Atual.ModoTemplates == ModoTemplates.PastaLocal;
    public string AvisoModoPasta =>
        $"Este PC está no modo Pasta local: as etiquetas abrem de \"{preferencias.Atual.PastaTemplates}\". " +
        "O acervo abaixo continua sendo o do banco. Troque o modo em Configurações.";

    public string PastaLocal => arquivos.PastaLocal;

    partial void OnSearchTermChanged(string value) => AplicarFiltro();
    partial void OnSomenteAlteradosChanged(bool value) => AplicarFiltro();
    partial void OnSelectedChanged(TemplateItem? value) => _ = CarregarVersoesAsync();

    // ── Ciclo de vida (a tela é recriada a cada navegação) ────────────────

    public void Ativar() => sync.DadosAtualizados += OnDadosAtualizados;
    public void Desativar() => sync.DadosAtualizados -= OnDadosAtualizados;

    private void OnDadosAtualizados() =>
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => { if (!IsBusy) Carregar(); });

    // ── Lista ─────────────────────────────────────────────────────────────

    [RelayCommand]
    public void Carregar()
    {
        try
        {
            var codigoSelecionado = Selected?.Codigo;
            _todos = arquivos.ListarComStatus().Select(t => new TemplateItem(t)).ToList();
            AplicarFiltro();
            Selected = Templates.FirstOrDefault(t => t.Codigo == codigoSelecionado);

            var alterados = _todos.Count(t => t.TemAlteracaoLocal);
            var naoBaixados = _todos.Count(t => t.Status is StatusArquivo.NaoBaixado or StatusArquivo.Desatualizado);
            Resumo = Contagem.Texto(_todos.Count, "template", "templates") +
                     (alterados > 0 ? $"  ·  {Contagem.Texto(alterados, "alterado neste PC", "alterados neste PC")}" : "") +
                     (naoBaixados > 0 ? $"  ·  {Contagem.Texto(naoBaixados, "a baixar", "a baixar")}" : "");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao carregar o acervo");
            StatusText = $"Erro ao carregar o acervo: {ex.Message}";
        }
    }

    private void AplicarFiltro()
    {
        var termo = SearchTerm.Trim();
        IEnumerable<TemplateItem> lista = _todos;

        if (SomenteAlterados)
            lista = lista.Where(t => t.TemAlteracaoLocal);

        if (termo.Length > 0)
            lista = lista.Where(t =>
                t.Codigo.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                t.NomeArquivo.Contains(termo, StringComparison.OrdinalIgnoreCase));

        Templates = new ObservableCollection<TemplateItem>(lista);
    }

    private async Task CarregarVersoesAsync()
    {
        Versoes.Clear();
        VersoesMensagem = null;
        if (Selected is not { } item || item.Local.Info.VersaoAtual == 0)
            return;

        try
        {
            foreach (var v in await arquivos.ListarVersoesAsync(item.Codigo))
                Versoes.Add(v);
        }
        catch (SemConexaoException)
        {
            VersoesMensagem = "Sem internet: as versões guardadas aparecem quando a conexão voltar.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao listar versões de {Codigo}", item.Codigo);
            VersoesMensagem = $"Não foi possível listar as versões: {ex.Message}";
        }
    }

    // ── Enviar arquivos ao acervo ─────────────────────────────────────────

    [RelayCommand]
    private async Task AdicionarArquivosAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Adicionar templates ao acervo",
            Filter = "BarTender Template (*.btw)|*.btw",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        await Ocupado("Enviando…", async () =>
        {
            int criados = 0, novas = 0, iguais = 0;
            foreach (var arquivo in dlg.FileNames)
            {
                var r = await arquivos.EnviarArquivoAsync(arquivo, "upload");
                switch (r.Resultado)
                {
                    case ResultadoEnvio.Criado: criados++; break;
                    case ResultadoEnvio.NovaVersao: novas++; break;
                    default: iguais++; break;
                }
                await RegistrarEnvio(r, "upload", arquivo);
            }
            StatusText = ResumoEnvio(criados, novas, iguais);
        });
    }

    [RelayCommand]
    private async Task ImportarPastaAsync()
    {
        var dlg = new OpenFolderDialog { Title = "Importar todos os .btw de uma pasta para o acervo" };
        if (dlg.ShowDialog() != true) return;

        await Ocupado("Lendo a pasta…", async () =>
        {
            var progresso = new Progress<(int Feitos, int Total)>(p => StatusText = $"Enviando {p.Feitos} de {p.Total}…");
            var r = await arquivos.EnviarPastaAsync(dlg.FolderName, progresso);

            await eventos.RegistrarAsync(Acoes.TemplateAlterado, detalhes: new
            {
                origem = "importacao_pasta",
                pasta = dlg.FolderName,
                criados = r.Criados,
                novasVersoes = r.NovasVersoes,
                identicos = r.Identicos,
                erros = r.Erros.Count
            });

            StatusText = ResumoEnvio(r.Criados, r.NovasVersoes, r.Identicos) +
                         (r.Duplicados > 0 ? $" {r.Duplicados} repetido(s) em subpastas: ficou o mais recente." : "") +
                         (r.Erros.Count > 0 ? $" {r.Erros.Count} com erro." : "");

            if (r.Erros.Count > 0)
                dialog.ShowWarning("Importar pasta", "Arquivos com erro:\n\n" + string.Join("\n", r.Erros.Take(20)));
        });
    }

    private bool TemSelecao() => Selected is not null;

    [RelayCommand(CanExecute = nameof(TemSelecao))]
    private async Task EnviarNovaVersaoAsync()
    {
        if (Selected is not { } item) return;

        var dlg = new OpenFileDialog
        {
            Title = $"Nova versão de {item.Codigo}",
            Filter = "BarTender Template (*.btw)|*.btw",
            Multiselect = false
        };
        if (dlg.ShowDialog() != true) return;

        await Ocupado("Enviando…", async () =>
        {
            var r = await arquivos.EnviarArquivoAsync(dlg.FileName, "upload", item.Codigo);
            await RegistrarEnvio(r, "upload", dlg.FileName);
            StatusText = r.Resultado == ResultadoEnvio.Identico
                ? $"{item.Codigo}: o arquivo é igual à versão atual (v{r.Versao}); nada foi alterado."
                : $"{item.Codigo}: versão v{r.Versao} enviada.";
        });
    }

    private bool PodeEnviarAlteracao() => Selected?.TemAlteracaoLocal == true;

    [RelayCommand(CanExecute = nameof(PodeEnviarAlteracao))]
    private async Task EnviarAlteracaoAsync()
    {
        if (Selected is not { } item) return;

        await Ocupado("Enviando alteração…", async () =>
        {
            var r = await arquivos.EnviarAlteracaoAsync(item.Codigo, forcar: false);

            if (r.Resultado == ResultadoEnvio.Conflito)
            {
                var quando = r.ConflitoEm?.UtcToLocal().ToString("dd/MM HH:mm") ?? "?";
                var substituir = dialog.AskConfirmation("Template alterado por outra pessoa",
                    $"{r.ConflitoPor ?? "Alguém"} enviou a v{r.Versao} de {item.Codigo} em {quando}, " +
                    "depois da versão que você editou.\n\n" +
                    "Enviar a sua alteração mesmo assim? A versão dela fica guardada como backup.");
                if (!substituir)
                {
                    StatusText = "Envio cancelado. Sua alteração continua neste PC.";
                    return;
                }
                r = await arquivos.EnviarAlteracaoAsync(item.Codigo, forcar: true);
            }

            await RegistrarEnvio(r, "edicao", item.Local.CaminhoLocal);
            StatusText = r.Resultado == ResultadoEnvio.Identico
                ? $"{item.Codigo}: o arquivo é igual à versão do banco; nada foi enviado."
                : $"{item.Codigo}: alteração enviada como v{r.Versao}.";
        });
    }

    [RelayCommand]
    private async Task RestaurarAsync(TemplateVersao? versao)
    {
        if (Selected is not { } item || versao is null) return;

        var aviso = item.TemAlteracaoLocal
            ? "\n\nAtenção: a alteração feita neste PC e ainda não enviada será substituída."
            : "";
        if (!dialog.AskConfirmation("Restaurar versão",
                $"Restaurar a v{versao.Versao} de {item.Codigo} ({versao.Resumo})?\n\n" +
                $"Ela vira uma versão nova; a atual fica guardada como backup.{aviso}"))
            return;

        await Ocupado("Restaurando…", async () =>
        {
            var r = await arquivos.RestaurarAsync(item.Codigo, versao.Versao);
            await eventos.RegistrarAsync(Acoes.TemplateAlterado, codigo: item.Codigo,
                detalhes: new { origem = "restauracao", restaurada = versao.Versao, novaVersao = r.Versao });
            StatusText = r.Resultado == ResultadoEnvio.Identico
                ? $"{item.Codigo}: a v{versao.Versao} já é igual à atual."
                : $"{item.Codigo}: v{versao.Versao} restaurada como v{r.Versao}.";
        });
    }

    // ── Abrir / exportar / sincronizar ────────────────────────────────────

    private bool PodeAbrir() => Selected?.PodeAbrir == true;

    [RelayCommand(CanExecute = nameof(PodeAbrir))]
    private void Abrir()
    {
        if (Selected?.Local.CaminhoLocal is not { } caminho) return;

        try
        {
            Process.Start(new ProcessStartInfo(caminho) { UseShellExecute = true });
            StatusText = $"Aberto no BarTender: {Path.GetFileName(caminho)}. Depois de salvar, use \"Enviar alteração\".";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao abrir {Caminho}", caminho);
            dialog.ShowError("Abrir template", $"Não foi possível abrir o arquivo:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ExportarAsync()
    {
        var dlg = new OpenFolderDialog { Title = "Exportar o acervo (versão atual de todos os templates) para a pasta" };
        if (dlg.ShowDialog() != true) return;

        await Ocupado("Exportando…", async () =>
        {
            var (copiados, faltando) = await arquivos.ExportarAsync(dlg.FolderName);
            StatusText = $"{copiados} template(s) exportado(s) para {dlg.FolderName}." +
                         (faltando > 0 ? $" {faltando} ainda não baixado(s) neste PC ficaram de fora." : "");
        });
    }

    [RelayCommand]
    private async Task SincronizarAsync()
    {
        await Ocupado("Sincronizando…", async () =>
        {
            var ok = await sync.SincronizarAgoraAsync();
            StatusText = ok ? "Acervo sincronizado." : "Sem internet: mostrando a cópia deste PC.";
        });
    }

    // ── Apoio ─────────────────────────────────────────────────────────────

    /// <summary>Roda a ação com a tela ocupada; depois sincroniza e recarrega a lista.</summary>
    private async Task Ocupado(string mensagem, Func<Task> acao)
    {
        IsBusy = true;
        StatusText = mensagem;
        try
        {
            await acao();
            await sync.SincronizarAgoraAsync();
        }
        catch (SemConexaoException ex)
        {
            StatusText = ex.Message;
            dialog.ShowWarning("Sem internet", ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro na tela Templates");
            StatusText = $"Erro: {ex.Message}";
            dialog.ShowError("Templates", ex.Message);
        }
        finally
        {
            IsBusy = false;
            Carregar(); // reseleciona o item, o que recarrega as versões
        }
    }

    private Task RegistrarEnvio(EnvioTemplate r, string origem, string? arquivo) =>
        r.Resultado is ResultadoEnvio.Criado or ResultadoEnvio.NovaVersao
            ? eventos.RegistrarAsync(Acoes.TemplateAlterado, codigo: r.Codigo,
                detalhes: new { origem, versao = r.Versao, arquivo })
            : Task.CompletedTask;

    private static string ResumoEnvio(int criados, int novas, int iguais) =>
        $"{Contagem.Texto(criados, "template novo", "templates novos")}, " +
        $"{Contagem.Texto(novas, "nova versão", "novas versões")}, " +
        $"{Contagem.Texto(iguais, "já igual (ignorado)", "já iguais (ignorados)")}.";
}
