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
        private readonly ILogger<HomeViewModel>? _logger;

        // Tempo que o check verde fica na tela antes de abrir o modal de resumo.
        private const int SuccessAnimationMs = 1600;

        private List<LabelRecord> _allRecords = new();
        private List<ImportBatchRecord> _allBatches = new();
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

        public bool IsOverlayVisible => IsImporting || IsSuccessAnimation || IsSummaryOpen;

        private ImportSummary? _summary;
        public ImportSummary? Summary
        {
            get => _summary;
            private set => SetProperty(ref _summary, value);
        }

        public string StatTotal => _allRecords.Count.ToString();
        public string StatVinculados => _allRecords.Count(l => l.HasFile).ToString();
        public string StatSemArquivo => _allRecords.Count(l => !l.HasFile).ToString();
        public string StatImportacoes => _allBatches.Count.ToString();
        public string StatInvoices => _allGroups.Count == 1 ? "1 invoice" : $"{_allGroups.Count} invoices";

        private int _statEtiquetasValue;
        public string StatEtiquetas => _statEtiquetasValue.ToString();

        private int _statFolhasValue;
        public string StatFolhas => _statFolhasValue.ToString();

        public ICommand LoadCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand OpenCommand { get; }
        public ICommand CloseSummaryCommand { get; }
        public ICommand OpenReportFolderCommand { get; }

        public HomeViewModel(
            IExcelImportService excelImportService,
            IInvoiceReportService reportService,
            ILabelRepository labelRepository,
            IDialogService dialog,
            ILogger<HomeViewModel>? logger = null)
        {
            _excelImportService = excelImportService;
            _reportService = reportService;
            _labelRepository = labelRepository;
            _dialog = dialog;
            _logger = logger;

            LoadCommand = new RelayCommand(async () => await LoadAsync());
            ImportCommand = new RelayCommand(async () => await ImportAsync(), () => !IsOverlayVisible);
            OpenCommand = new RelayCommand(async p => await OpenAsync(p));
            CloseSummaryCommand = new RelayCommand(() => IsSummaryOpen = false);
            OpenReportFolderCommand = new RelayCommand(OpenReportFolder);
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

        public async Task LoadAsync()
        {
            _allRecords = (await _labelRepository.GetAllAsync()).ToList();
            _allBatches = (await _labelRepository.GetBatchesAsync()).ToList();

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

            // Folhas de espelho: calculadas por invoice e somadas (cada invoice
            // começa uma folha nova). Não dependem da busca, para o número não
            // oscilar enquanto o usuário procura um item específico.
            _statEtiquetasValue = _allRecords.Count;
            _statFolhasValue = _allGroups.Sum(g => g.Folhas);

            ApplyFilters();
            RaisePropertyChanged(nameof(StatTotal));
            RaisePropertyChanged(nameof(StatVinculados));
            RaisePropertyChanged(nameof(StatSemArquivo));
            RaisePropertyChanged(nameof(StatImportacoes));
            RaisePropertyChanged(nameof(StatEtiquetas));
            RaisePropertyChanged(nameof(StatFolhas));
            RaisePropertyChanged(nameof(StatInvoices));
        }

        private void ApplyFilters()
        {
            var termo = SearchTerm?.Trim() ?? string.Empty;

            Invoices.Clear();
            foreach (var group in _allGroups)
            {
                if (!group.ApplyFilter(termo))
                    continue;

                // Buscando: abre as invoices encontradas para mostrar o rótulo.
                if (termo.Length > 0)
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

                    summary = ImportSummary.From(dialog.FileName, result, pasta, quantidade, erroRelatorio);

                    // A fila antiga já foi substituída no banco: limpa a busca para
                    // a planilha nova aparecer inteira e recarrega a grid.
                    SearchTerm = string.Empty;
                    await LoadAsync();
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

                    _dialog.ShowWarning("Sem template",
                        $"Esta etiqueta (Invoice: {label.Invoice}, Código: {label.Codigo}) não possui template vinculado.\n\n" +
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

                // Abre com o programa padrão do Windows (.btw → BarTender, .pdf → leitor de PDF).
                _logger?.LogInformation("[ABRIR] Abrindo arquivo com programa padrão: {Path}", label.LabelFilePath);
                Process.Start(new ProcessStartInfo(label.LabelFilePath!)
                {
                    UseShellExecute = true
                });
                _logger?.LogInformation("[ABRIR] Arquivo aberto com sucesso");

                if (!label.FoiAberta)
                {
                    try
                    {
                        await _labelRepository.MarcarComoAbertaAsync(label.Id);
                        // Notifica a linha (botão "Aberto") e a barra de progresso da invoice.
                        label.AbertaEm = DateTime.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        // Não fatal: o arquivo já foi aberto, só a marcação falhou.
                        _logger?.LogWarning(ex, "[ABRIR] Falha ao marcar etiqueta Id={Id} como aberta", label.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[ABRIR] Exceção inesperada ao abrir etiqueta: {Invoice}", label?.Invoice);
                _dialog.ShowError("Erro ao abrir etiqueta",
                    $"Ocorreu um erro inesperado ao tentar abrir o template.\n\n" +
                    $"Detalhes: {ex.Message}\n\n" +
                    $"Etiqueta: {label?.Invoice}");
            }
        }
    }
}
