using Busca_BT.Data;
using Busca_BT.Models;
using Busca_BT.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Busca_BT.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Busca_BT.ViewModels
{
    public sealed class HomeViewModel : BaseViewModel
    {
        private readonly IExcelImportService _excelImportService;
        private readonly IInvoiceReportService _reportService;
        private readonly ILabelRepository _labelRepository;
        private readonly IDialogService _dialog;
        private readonly ISessaoOperador _sessao;
        private readonly IEventoService _eventos;
        private readonly ISyncService _sync;
        private readonly ITemplateArquivos _templates;
        private readonly ILogger<HomeViewModel>? _logger;

        /// <summary>Importar planilha e limpar a fila: só administrador.</summary>
        public bool IsAdmin => _sessao.IsAdmin;

        // Tempo que o check verde fica na tela antes de abrir o modal de resumo.
        private const int SuccessAnimationMs = 1600;

        private List<LabelRecord> _allRecords = new();
        private List<InvoiceGroupViewModel> _allGroups = new();

        /// <summary>Invoices exibidas na Home (uma linha por invoice, expansível).</summary>
        public ObservableCollection<InvoiceGroupViewModel> Invoices { get; } = new();

        private string _searchTerm = string.Empty;
        public string SearchTerm
        {
            get => _searchTerm;
            set
            {
                if (SetProperty(ref _searchTerm, value))
                    ApplyFilters();
            }
        }

        // Mostra só os rótulos com pendência (sem template, data inválida…).
        private bool _somentePendencias;
        public bool SomentePendencias
        {
            get => _somentePendencias;
            set
            {
                if (SetProperty(ref _somentePendencias, value))
                    ApplyFilters();
            }
        }

        /// <summary>Rótulos da fila com alguma pendência.</summary>
        public int TotalPendencias => _allRecords.Count(l => l.TemPendencia);
        public bool HasPendencias => TotalPendencias > 0;
        public string PendenciasTexto => TotalPendencias == 1
            ? "1 item com pendência"
            : $"{TotalPendencias.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))} itens com pendência";

        // ── Estado do overlay de importação ──────────────────────────────────
        // Processando (spinner) → Sucesso (check verde animado) → Resumo (modal).
        // Em caso de falha vai direto do Processando para o Resumo.

        private bool _isImporting;
        public bool IsImporting
        {
            get => _isImporting;
            private set { if (SetProperty(ref _isImporting, value)) RaisePropertyChanged(nameof(IsOverlayVisible)); }
        }

        private bool _isSuccessAnimation;
        public bool IsSuccessAnimation
        {
            get => _isSuccessAnimation;
            private set { if (SetProperty(ref _isSuccessAnimation, value)) RaisePropertyChanged(nameof(IsOverlayVisible)); }
        }

        private bool _isSummaryOpen;
        public bool IsSummaryOpen
        {
            get => _isSummaryOpen;
            private set { if (SetProperty(ref _isSummaryOpen, value)) RaisePropertyChanged(nameof(IsOverlayVisible)); }
        }

        // Modal de confirmação de "Limpar fila".
        private bool _isConfirmClearOpen;
        public bool IsConfirmClearOpen
        {
            get => _isConfirmClearOpen;
            private set { if (SetProperty(ref _isConfirmClearOpen, value)) RaisePropertyChanged(nameof(IsOverlayVisible)); }
        }

        private bool _isClearing;
        public bool IsClearing
        {
            get => _isClearing;
            private set => SetProperty(ref _isClearing, value);
        }

        // Modal de ciência: rótulo com pendência só abre depois de marcar todas as caixas.
        private bool _isCienciaOpen;
        public bool IsCienciaOpen
        {
            get => _isCienciaOpen;
            private set { if (SetProperty(ref _isCienciaOpen, value)) RaisePropertyChanged(nameof(IsOverlayVisible)); }
        }

        private LabelRecord? _cienciaLabel;
        public LabelRecord? CienciaLabel
        {
            get => _cienciaLabel;
            private set => SetProperty(ref _cienciaLabel, value);
        }

        /// <summary>Uma caixa de seleção por pendência do rótulo que o usuário quer abrir.</summary>
        public ObservableCollection<CienciaItem> CienciaItens { get; } = new();

        public bool IsOverlayVisible => IsImporting || IsSuccessAnimation || IsSummaryOpen || IsConfirmClearOpen || IsCienciaOpen;

        public bool HasFila => _allRecords.Count > 0;

        public string ConfirmClearTexto =>
            $"{Contagem.Invoices(_allGroups.Count)} ({Contagem.Itens(_allRecords.Count)}, " +
            $"{Contagem.Etiquetas(TotalEtiquetas)}) serão removidas da fila, " +
            "incluindo o progresso dos rótulos já abertos. O histórico de importações e os relatórios salvos não são apagados.";

        /// <summary>Etiquetas físicas da fila inteira: soma da Qtd Invoice.</summary>
        public int TotalEtiquetas => _allGroups.Sum(g => g.TotalEtiquetas);

        /// <summary>Pacotes da fila inteira: LPNs diferentes.</summary>
        public int TotalPacotes => LpnOrdem.ContarPacotes(_allRecords);

        /// <summary>Linha de resumo da Home: "7 invoices · 1.042 etiquetas · 9 pacotes no total".</summary>
        public string ResumoFila =>
            $"{Contagem.Invoices(_allGroups.Count)}  ·  {Contagem.Etiquetas(TotalEtiquetas)}  ·  {Contagem.Pacotes(TotalPacotes)} no total";

        private ImportSummary? _summary;
        public ImportSummary? Summary
        {
            get => _summary;
            private set => SetProperty(ref _summary, value);
        }

        public ICommand LoadCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand OpenCommand { get; }
        public ICommand CloseSummaryCommand { get; }
        public ICommand OpenReportFolderCommand { get; }
        public ICommand AskClearQueueCommand { get; }
        public ICommand CancelClearQueueCommand { get; }
        public ICommand ConfirmClearQueueCommand { get; }
        public ICommand CancelCienciaCommand { get; }
        public ICommand ConfirmCienciaCommand { get; }

        public HomeViewModel(
            IExcelImportService excelImportService,
            IInvoiceReportService reportService,
            ILabelRepository labelRepository,
            IDialogService dialog,
            ISessaoOperador sessao,
            IEventoService eventos,
            ISyncService sync,
            ITemplateArquivos templates,
            ILogger<HomeViewModel>? logger = null)
        {
            _excelImportService = excelImportService;
            _reportService = reportService;
            _labelRepository = labelRepository;
            _dialog = dialog;
            _sessao = sessao;
            _eventos = eventos;
            _sync = sync;
            _templates = templates;
            _logger = logger;

            LoadCommand = new RelayCommand(async () => await LoadAsync());
            ImportCommand = new RelayCommand(async () => await ImportAsync(), () => IsAdmin && !IsOverlayVisible);
            OpenCommand = new RelayCommand(async p => await OpenAsync(p));
            CloseSummaryCommand = new RelayCommand(() => IsSummaryOpen = false);
            OpenReportFolderCommand = new RelayCommand(OpenReportFolder);

            AskClearQueueCommand = new RelayCommand(() =>
            {
                RaisePropertyChanged(nameof(ConfirmClearTexto));
                IsConfirmClearOpen = true;
            }, () => IsAdmin && HasFila && !IsOverlayVisible);
            CancelClearQueueCommand = new RelayCommand(() => IsConfirmClearOpen = false, () => !IsClearing);
            ConfirmClearQueueCommand = new RelayCommand(async () => await ClearQueueAsync(), () => !IsClearing);

            CancelCienciaCommand = new RelayCommand(FecharCiencia);
            ConfirmCienciaCommand = new RelayCommand(async () => await ConfirmarCienciaAsync(),
                () => CienciaItens.Count > 0 && CienciaItens.All(i => i.Ciente));
        }

        private async Task ClearQueueAsync()
        {
            IsClearing = true;
            try
            {
                var removidos = await _labelRepository.ClearQueueAsync();
                await _eventos.RegistrarAsync(Acoes.LimpouFila, detalhes: new { itens = removidos });
                SearchTerm = string.Empty;
                await LoadAsync();
                IsConfirmClearOpen = false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[LIMPAR FILA] Falha ao limpar a fila");
                IsConfirmClearOpen = false;
                _dialog.ShowError("Limpar fila", $"Não foi possível limpar a fila.\n\n{ex.Message}");
            }
            finally
            {
                IsClearing = false;
            }
        }

        private void OpenReportFolder()
        {
            if (Summary?.RelatorioPasta is not { } pasta || !Directory.Exists(pasta))
                return;

            try
            {
                Process.Start(new ProcessStartInfo(pasta) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Falha ao abrir a pasta de relatórios {Pasta}", pasta);
                _dialog.ShowWarning("Relatórios", $"Não foi possível abrir a pasta:\n{pasta}");
            }
        }

        /// <summary>Tela visível: passa a recarregar sozinha quando chegam dados novos.</summary>
        public void Ativar() => _sync.DadosAtualizados += OnDadosAtualizados;

        /// <summary>Tela saiu de cena: para de ouvir a sincronização (a tela é recriada a cada navegação).</summary>
        public void Desativar() => _sync.DadosAtualizados -= OnDadosAtualizados;

        private void OnDadosAtualizados()
        {
            // Vem da thread da sincronização. Não recarrega no meio de uma importação/modal.
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
            {
                if (!IsOverlayVisible)
                    await LoadAsync();
            });
        }

        public async Task LoadAsync()
        {
            _allRecords = (await _labelRepository.GetAllAsync()).ToList();

            // Vínculo com os templates + pendências de cada rótulo. Se a leitura do acervo
            // falhar, a fila aparece mesmo assim (todos sinalizados como sem template).
            IReadOnlyList<LabelRecord> templates;
            try
            {
                // Modo Banco: cópia local do acervo; modo Pasta local: arquivos da pasta escolhida.
                templates = await Task.Run(_templates.ObterParaVinculo);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[FILA] Falha ao ler os templates; a fila será exibida sem vínculo");
                templates = [];
            }
            await Task.Run(() => LabelDiagnostics.Aplicar(_allRecords, templates, DateTime.Today, _templates.MensagemArquivoAusente));

            if (!HasPendencias)
                _somentePendencias = false;

            // Uma linha por invoice, na ordem em que aparecem na planilha.
            // Mantém abertas as invoices que o usuário já tinha expandido.
            var expandidas = _allGroups.Where(g => g.IsExpanded).Select(g => g.Invoice)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _allGroups = _allRecords
                .GroupBy(l => l.Invoice, StringComparer.OrdinalIgnoreCase)
                .Select(g => new InvoiceGroupViewModel(g.Key, g.ToList())
                {
                    IsExpanded = expandidas.Contains(g.Key)
                })
                .ToList();

            ApplyFilters();
            // Totais da fila inteira (não mudam com a busca).
            RaisePropertyChanged(nameof(ResumoFila));
            RaisePropertyChanged(nameof(HasFila));
            RaisePropertyChanged(nameof(TotalPendencias));
            RaisePropertyChanged(nameof(HasPendencias));
            RaisePropertyChanged(nameof(PendenciasTexto));
            RaisePropertyChanged(nameof(SomentePendencias));
            CommandManager.InvalidateRequerySuggested(); // "Limpar fila" liga/desliga conforme a fila
        }

        private void ApplyFilters()
        {
            var termo = SearchTerm?.Trim() ?? string.Empty;

            Invoices.Clear();
            foreach (var group in _allGroups)
            {
                if (!group.ApplyFilter(termo, SomentePendencias))
                    continue;

                // Buscando: abre as invoices encontradas para mostrar o rótulo.
                if (termo.Length > 0 || SomentePendencias)
                    group.IsExpanded = true;

                Invoices.Add(group);
            }
        }

        private async Task ImportAsync()
        {
            _logger?.LogInformation("[IMPORTAÇÃO] Usuário clicou em IMPORTAR PLANILHA");

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Planilhas Excel (*.xlsx)|*.xlsx",
                Title = "Selecionar planilha para importar"
            };

            if (dialog.ShowDialog() != true)
            {
                _logger?.LogInformation("[IMPORTAÇÃO] Usuário cancelou a seleção de arquivo");
                return;
            }

            _logger?.LogInformation("[IMPORTAÇÃO] Arquivo selecionado: {FilePath}", dialog.FileName);

            IsSummaryOpen = false;
            IsImporting = true;

            ImportSummary summary;
            try
            {
                var result = await _excelImportService.ImportAsync(dialog.FileName);

                if (result.Success)
                {
                    _logger?.LogInformation(
                        "[IMPORTAÇÃO] Concluída. Total={Total} | Importadas={Imported} | Ignoradas={Skipped}",
                        result.TotalRows, result.ImportedRows, result.SkippedRows);

                    // Relatório .xlsx por invoice, salvo automaticamente em Downloads.
                    // Uma falha aqui não desfaz a importação — só é avisada no modal.
                    string? pasta = null, erroRelatorio = null;
                    var quantidade = 0;
                    try
                    {
                        var relatorio = await _reportService.GerarAsync(result.Imported, dialog.FileName);
                        pasta = relatorio.Pasta;
                        quantidade = relatorio.Arquivos.Count;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "[IMPORTAÇÃO] Falha ao gerar relatórios por invoice");
                        erroRelatorio = $"Não foi possível salvar os relatórios: {ex.Message}";
                    }

                    // A fila antiga já foi substituída no banco: limpa a busca para
                    // a planilha nova aparecer inteira e recarrega a grid.
                    SearchTerm = string.Empty;
                    await LoadAsync();

                    // Pendências calculadas no carregamento (inclui o vínculo com os templates).
                    summary = ImportSummary.From(dialog.FileName, result, pasta, quantidade, erroRelatorio,
                        _allRecords.Where(l => l.TemPendencia).ToList());

                    await _eventos.RegistrarAsync(Acoes.Importou, detalhes: new
                    {
                        arquivo = Path.GetFileName(dialog.FileName),
                        importados = result.ImportedRows,
                        ignorados = result.SkippedRows,
                        comPendencia = summary.ComPendencia.Count,
                        invoices = result.Imported.Select(l => l.Invoice).Distinct().ToList()
                    });
                }
                else
                {
                    summary = ImportSummary.From(dialog.FileName, result);
                    _logger?.LogError("[IMPORTAÇÃO] Erro na importação: {ErrorMessage}", result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[IMPORTAÇÃO] Exceção inesperada durante importação");
                summary = ImportSummary.FromException(dialog.FileName, ex);
            }
            finally
            {
                IsImporting = false;
            }

            Summary = summary;

            if (summary.Success)
            {
                IsSuccessAnimation = true;
                await Task.Delay(SuccessAnimationMs);
                IsSuccessAnimation = false;
            }

            IsSummaryOpen = true;
        }

        private async Task OpenAsync(object? param)
        {
            if (param is not LabelRecord label)
            {
                _logger?.LogWarning("[ABRIR] Parâmetro inválido - não é LabelRecord");
                return;
            }

            _logger?.LogInformation("[ABRIR] Usuário clicou em ABRIR para etiqueta: {Invoice} ({Codigo})", label.Invoice, label.Codigo);

            try
            {
                if (!label.HasFile)
                {
                    _logger?.LogWarning("[ABRIR] Etiqueta SEM template | Invoice: {Invoice} | Código: {Codigo}",
                        label.Invoice, label.Codigo);

                    var motivos = label.TemPendencia
                        ? string.Join("\n", label.Pendencias.Select(p => "• " + p))
                        : "Nenhum template vinculado.";

                    _dialog.ShowWarning("Não foi possível abrir",
                        $"Invoice: {label.Invoice}\nCódigo: {label.Codigo}\n\n{motivos}\n\n" +
                        "Cadastre um arquivo .btw com esse código na tela Templates.");
                    return;
                }

                if (!File.Exists(label.LabelFilePath))
                {
                    _logger?.LogError("[ABRIR] ARQUIVO NÃO EXISTE NO DISCO! Caminho: {Path}", label.LabelFilePath);

                    _dialog.ShowWarning("Arquivo não encontrado",
                        $"O arquivo do template foi movido ou deletado.\n\n" +
                        $"Etiqueta: {label.Invoice}\n" +
                        $"Caminho: {label.LabelFilePath}\n\n" +
                        "Verifique o arquivo no caminho indicado ou reimporte os dados.");
                    return;
                }

                // Rótulo com pendência (data inválida, vencido, campo vazio…): o template está
                // vinculado, mas só abre depois que o usuário confirmar ciência de cada aviso.
                if (label.TemPendencia)
                {
                    _logger?.LogInformation("[ABRIR] Etiqueta com pendência — pedindo ciência | Invoice: {Invoice} | Código: {Codigo}",
                        label.Invoice, label.Codigo);

                    CienciaItens.Clear();
                    foreach (var pendencia in label.Pendencias)
                        CienciaItens.Add(new CienciaItem(pendencia));
                    CienciaLabel = label;
                    IsCienciaOpen = true;
                    return;
                }

                await AbrirArquivoAsync(label);
            }
            catch (Exception ex)
            {
                MostrarErroAoAbrir(label, ex);
            }
        }

        private void FecharCiencia()
        {
            IsCienciaOpen = false;
            CienciaLabel = null;
            CienciaItens.Clear();
        }

        private async Task ConfirmarCienciaAsync()
        {
            if (CienciaLabel is not { } label || !CienciaItens.All(i => i.Ciente))
                return;

            _logger?.LogWarning(
                "[ABRIR] Usuário confirmou ciência das pendências e abriu mesmo assim | Invoice: {Invoice} | Código: {Codigo} | Pendências: {Pendencias}",
                label.Invoice, label.Codigo, string.Join(" | ", label.Pendencias));

            FecharCiencia();

            await _eventos.RegistrarAsync(Acoes.Ciencia, label.Invoice, label.Codigo,
                new { item = label.Item, lpn = label.Lpn, pendencias = label.Pendencias });

            try
            {
                await AbrirArquivoAsync(label);
            }
            catch (Exception ex)
            {
                MostrarErroAoAbrir(label, ex);
            }
        }

        private async Task AbrirArquivoAsync(LabelRecord label)
        {
            // Modo Banco: se houver internet e versão mais nova do template, baixa antes de abrir.
            await _templates.GarantirAtualizadoAsync(label.LabelFilePath!);

            // Abre com o programa padrão do Windows (.btw → BarTender, .pdf → leitor de PDF).
            _logger?.LogInformation("[ABRIR] Abrindo arquivo com programa padrão: {Path}", label.LabelFilePath);
            Process.Start(new ProcessStartInfo(label.LabelFilePath!)
            {
                UseShellExecute = true
            });
            _logger?.LogInformation("[ABRIR] Arquivo aberto com sucesso");

            await _eventos.RegistrarAsync(Acoes.Abriu, label.Invoice, label.Codigo,
                new { item = label.Item, lpn = label.Lpn, lote = label.Lote, arquivo = label.LabelFilePath, comPendencia = label.TemPendencia });

            if (!label.FoiAberta)
            {
                try
                {
                    await _labelRepository.MarcarComoAbertaAsync(label.Id);
                    // Notifica a linha (botão "Aberto") e a barra de progresso da invoice.
                    label.AbertaPor = _sessao.Atual?.Nome;
                    label.AbertaEm = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    // Não fatal: o arquivo já foi aberto, só a marcação falhou.
                    _logger?.LogWarning(ex, "[ABRIR] Falha ao marcar etiqueta Id={Id} como aberta", label.Id);
                }
            }
        }

        private void MostrarErroAoAbrir(LabelRecord label, Exception ex)
        {
            _logger?.LogError(ex, "[ABRIR] Exceção inesperada ao abrir etiqueta: {Invoice}", label.Invoice);
            _dialog.ShowError("Erro ao abrir etiqueta",
                $"Ocorreu um erro inesperado ao tentar abrir o template.\n\n" +
                $"Detalhes: {ex.Message}\n\n" +
                $"Etiqueta: {label.Invoice}");
        }
    }
}
