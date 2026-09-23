using Busca_BT.Data;
using Busca_BT.Models;
using Busca_BT.Services;
using Microsoft.Win32;
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
        private readonly ILabelRepository _labelRepository;
        private readonly IBartenderService _bartenderService;
        private readonly INavigationService _navigation;
        private readonly EtiquetasOptions _options;
        private readonly IDialogService _dialog;
        private readonly ILogger<HomeViewModel>? _logger;

        private List<LabelRecord> _allRecords = new();
        private List<ImportBatchRecord> _allBatches = new();

        public ObservableCollection<LabelRecord> Items { get; } = new();
        public ObservableCollection<BatchOption> Batches { get; } = new();

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

        private BatchOption? _selectedBatch;
        public BatchOption? SelectedBatch
        {
            get => _selectedBatch;
            set
            {
                if (SetProperty(ref _selectedBatch, value))
                    ApplyFilters();
            }
        }

        private DateTime _dataUltimaCarga = DateTime.MinValue;
        public DateTime DataUltimaCarga
        {
            get => _dataUltimaCarga;
            set => SetProperty(ref _dataUltimaCarga, value);
        }

        private bool _temAtualizacaoPendente;
        public bool TemAtualizacaoPendente
        {
            get => _temAtualizacaoPendente;
            set => SetProperty(ref _temAtualizacaoPendente, value);
        }

        public string StatTotal => _allRecords.Count.ToString();
        public string StatVinculados => _allRecords.Count(l => l.HasFile).ToString();
        public string StatSemArquivo => _allRecords.Count(l => !l.HasFile).ToString();
        public string StatImportacoes => _allBatches.Count.ToString();

        private int _statEtiquetasValue;
        public string StatEtiquetas => _statEtiquetasValue.ToString();

        private int _statFolhasValue;
        public string StatFolhas => _statFolhasValue.ToString();

        public ICommand LoadCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand OpenCommand { get; }
        public ICommand SyncCommand { get; }

        public HomeViewModel(
            IExcelImportService excelImportService,
            ILabelRepository labelRepository,
            IBartenderService bartenderService,
            INavigationService navigation,
            EtiquetasOptions options,
            IDialogService dialog,
            ILogger<HomeViewModel>? logger = null)
        {
            _excelImportService = excelImportService;
            _labelRepository = labelRepository;
            _bartenderService = bartenderService;
            _navigation = navigation;
            _options = options;
            _dialog = dialog;
            _logger = logger;

            LoadCommand = new RelayCommand(async () => await LoadAsync());
            ImportCommand = new RelayCommand(async () => await ImportAsync());
            OpenCommand = new RelayCommand(async p => await OpenAsync(p));
            SyncCommand = new RelayCommand(async () => await SyncAsync());
        }

        public async Task LoadAsync()
        {
            _allRecords = (await _labelRepository.GetAllAsync()).ToList();
            _allBatches = (await _labelRepository.GetBatchesAsync()).ToList();

            Batches.Clear();
            Batches.Add(new BatchOption(null, $"Todas as importações  ({_allBatches.Count})"));
            foreach (var b in _allBatches.OrderByDescending(b => b.ImportedAt))
                Batches.Add(new BatchOption(b.Id, b.Resumo));

            // Update load timestamp and check for pending updates
            DataUltimaCarga = DateTime.UtcNow;
            TemAtualizacaoPendente = false;

            ApplyFilters();
            RaisePropertyChanged(nameof(StatTotal));
            RaisePropertyChanged(nameof(StatVinculados));
            RaisePropertyChanged(nameof(StatSemArquivo));
            RaisePropertyChanged(nameof(StatImportacoes));
        }

        public async Task VerificarNovosTemplatesAsync()
        {
            try
            {
                var latestUpdate = await _labelRepository.GetLatestUpdateTimestampAsync();
                if (latestUpdate.HasValue && latestUpdate.Value > DataUltimaCarga)
                {
                    TemAtualizacaoPendente = true;
                }
            }
            catch (Exception ex)
            {
                // Log but don't show error — this is a background check
                System.Diagnostics.Debug.WriteLine($"Erro ao verificar novos templates: {ex.Message}");
            }
        }

        private void ApplyFilters()
        {
            var termo = SearchTerm?.Trim() ?? string.Empty;
            var batchId = SelectedBatch?.Id;

            var doLote = _allRecords.AsEnumerable();
            if (batchId.HasValue)
                doLote = doLote.Where(l => l.BatchId == batchId.Value);
            doLote = doLote.ToList();

            // Folhas de espelho: cada ITEM/linha da planilha é 1 etiqueta colada,
            // escopadas ao lote selecionado (não ao texto de busca, para o número
            // não oscilar enquanto o usuário procura um item específico).
            _statEtiquetasValue = doLote.Count();
            _statFolhasValue = EspelhoCalculator.CalcularFolhas(_statEtiquetasValue);
            RaisePropertyChanged(nameof(StatEtiquetas));
            RaisePropertyChanged(nameof(StatFolhas));

            var filtrado = doLote;
            if (!string.IsNullOrEmpty(termo))
                filtrado = filtrado.Where(l =>
                    l.Invoice.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                    l.Codigo.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                    l.Lote.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                    l.Lpn.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                    l.DescricaoAnvisa.Contains(termo, StringComparison.OrdinalIgnoreCase));

            Items.Clear();
            foreach (var it in filtrado)
                Items.Add(it);
        }

        private async Task SyncAsync()
        {
            await LoadAsync();
        }

        private async Task ImportAsync()
        {
            _logger?.LogInformation("🔵 [IMPORTAÇÃO] Usuário clicou em IMPORTAR PLANILHA");
            
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Planilhas Excel (*.xlsx)|*.xlsx",
                Title = "Selecionar planilha para importar"
            };

            if (dialog.ShowDialog() != true)
            {
                _logger?.LogInformation("🔴 [IMPORTAÇÃO] Usuário cancelou a seleção de arquivo");
                return;
            }

            _logger?.LogInformation("🟡 [IMPORTAÇÃO] Arquivo selecionado: {FilePath}", dialog.FileName);

            try
            {
                _logger?.LogInformation("🟡 [IMPORTAÇÃO] Iniciando processo de importação...");
                var result = await _excelImportService.ImportAsync(dialog.FileName);
                
                if (result.Success)
                {
                    _logger?.LogInformation("🟢 [IMPORTAÇÃO] ✅ Importação bem-sucedida!");
                    _logger?.LogInformation("   - Total de linhas: {Total}", result.TotalRows);
                    _logger?.LogInformation("   - Importadas: {Imported}", result.ImportedRows);
                    _logger?.LogInformation("   - Ignoradas: {Skipped}", result.SkippedRows);
                    _logger?.LogInformation("   - Arquivos vinculados: {Associated}", result.AssociatedFiles);
                    
                    await LoadAsync();

                    // Mensagem limpa e direta! A vinculação agora é feita pelo banco de dados (Mala Direta)
                    string detailsMessage = $"Arquivo: {Path.GetFileName(dialog.FileName)}\n" +
                        $"Importados: {result.ImportedRows}\n" +
                        $"Ignorados: {result.SkippedRows}\n\n" +
                        $"✅ Fila atualizada e cruzada com o Acervo de etiquetas com sucesso!";

                    _logger?.LogInformation("🟢 [IMPORTAÇÃO] Importação concluída. Vinculação gerenciada pelo Banco de Dados.");

                    _dialog.ShowInfo("Importação concluída", detailsMessage);
                }
                else
                {
                    _logger?.LogError("🔴 [IMPORTAÇÃO] Erro na importação: {ErrorMessage}", result.ErrorMessage);
                    _dialog.ShowError("Erro na importação",
                        (result.ErrorMessage ?? "Erro ao importar.") + "\n\nOs dados antigos foram preservados.");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "🔴 [IMPORTAÇÃO] Exceção inesperada durante importação");
                _dialog.ShowError("Erro inesperado", ex.Message + "\n\nOs dados antigos foram preservados.");
            }
        }

        private async Task OpenAsync(object? param)
        {
            if (param is not LabelRecord label)
            {
                _logger?.LogWarning("🟡 [ABRIR] Parâmetro inválido - não é LabelRecord");
                return;
            }

            _logger?.LogInformation("🔵 [ABRIR] Usuário clicou em ABRIR para etiqueta: {Invoice} ({Codigo})", label.Invoice, label.Codigo);

            try
            {
                // Validação mais detalhada do arquivo
                if (!label.HasFile)
                {
                    if (string.IsNullOrWhiteSpace(label.LabelFilePath))
                    {
                        _logger?.LogWarning("🟠 [ABRIR] ❌ Etiqueta SEM template - LabelFilePath está vazio");
                        _logger?.LogWarning("   Invoice: {Invoice} | Código: {Codigo}", label.Invoice, label.Codigo);
                        
                        _dialog.ShowWarning("Sem template", 
                            $"Esta etiqueta (Invoice: {label.Invoice}, Código: {label.Codigo}) não possui template vinculado.\n\n" +
                            "Nenhum arquivo foi associado durante a importação.");
                    }
                    else
                    {
                        _logger?.LogWarning("🟠 [ABRIR] ❌ LabelFilePath não está vazio mas HasFile=false: {Path}", label.LabelFilePath);
                        
                        _dialog.ShowWarning("Arquivo não encontrado",
                            $"O template não está acessível.\n\n" +
                            $"Etiqueta: {label.Invoice}\n" +
                            $"Arquivo esperado: {label.LabelFilePath}");
                    }
                    return;
                }

                _logger?.LogInformation("🟡 [ABRIR] Etiqueta HAS FILE. Verificando existência do arquivo...");
                _logger?.LogInformation("   Caminho: {Path}", label.LabelFilePath);
                _logger?.LogInformation("   É BTW: {IsBtw} | É PDF: {IsPdf}", label.IsBtw, label.IsPdf);

                // Verificar se o arquivo ainda existe no disco
                if (!File.Exists(label.LabelFilePath))
                {
                    _logger?.LogError("🔴 [ABRIR] ❌ ARQUIVO NÃO EXISTE NO DISCO! Caminho: {Path}", label.LabelFilePath);
                    
                    _dialog.ShowWarning("Arquivo não encontrado",
                        $"O arquivo do template foi movido ou deletado.\n\n" +
                        $"Etiqueta: {label.Invoice}\n" +
                        $"Caminho: {label.LabelFilePath}\n\n" +
                        "Verifique o arquivo no caminho indicado ou reimporte os dados.");
                    return;
                }

                _logger?.LogInformation("🟢 [ABRIR] ✅ Arquivo ENCONTRADO no disco!");

                // For .btw files, use ProcessStartInfo with UseShellExecute to open with default program (BarTender)
                if (label.IsBtw)
                {
                    _logger?.LogInformation("🟡 [ABRIR] Abrindo arquivo BTW com BarTender: {Path}", label.LabelFilePath);
                    Process.Start(new ProcessStartInfo(label.LabelFilePath!)
                    {
                        UseShellExecute = true
                    });
                    _logger?.LogInformation("🟢 [ABRIR] ✅ BarTender iniciado com sucesso");
                }
                else if (label.IsPdf)
                {
                    _logger?.LogInformation("🟡 [ABRIR] Abrindo arquivo PDF com BartenderService: {Path}", label.LabelFilePath);
                    var result = await _bartenderService.OpenPdfAsync(label);
                    if (result?.Success == false)
                    {
                        _logger?.LogError("🔴 [ABRIR] Erro ao abrir PDF: {Message}", result.Message);
                        _dialog.ShowWarning("Erro", result.Message ?? "Falha ao abrir o arquivo.");
                    }
                    else
                    {
                        _logger?.LogInformation("🟢 [ABRIR] ✅ PDF aberto com sucesso");
                    }
                }
                else
                {
                    _logger?.LogInformation("🟡 [ABRIR] Abrindo arquivo com programa padrão: {Path}", label.LabelFilePath);
                    Process.Start(new ProcessStartInfo(label.LabelFilePath!)
                    {
                        UseShellExecute = true
                    });
                    _logger?.LogInformation("🟢 [ABRIR] ✅ Arquivo aberto com sucesso");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "🔴 [ABRIR] Exceção inesperada ao abrir etiqueta: {Invoice}", label?.Invoice);
                _dialog.ShowError("Erro ao abrir etiqueta", 
                    $"Ocorreu um erro inesperado ao tentar abrir o template.\n\n" +
                    $"Detalhes: {ex.Message}\n\n" +
                    $"Etiqueta: {label?.Invoice}");
            }
        }
    }
}
