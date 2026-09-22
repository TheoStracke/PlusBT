using Busca_BT.Data;
using Busca_BT.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Busca_BT.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// Item de apresentação na grid
// ─────────────────────────────────────────────────────────────────────────────

public sealed class TemplateItem
{
    public int Id { get; init; }
    public string Codigo { get; init; } = string.Empty;
    public string DescricaoAnvisa { get; init; } = string.Empty;
    public string? LabelFilePath { get; init; }
    public DateTime? UpdatedAt { get; init; }

    // ── Propriedades calculadas para a View ───────────────────────────────
    public string NomeArquivo => Path.GetFileName(LabelFilePath ?? string.Empty);
    public string UpdatedAtFormatado => UpdatedAt?.ToString("dd/MM/yyyy HH:mm") ?? "—";
    public bool TemArquivo => File.Exists(LabelFilePath);

    public string StatusTexto => TemArquivo ? "Associada" : "Sem arquivo";
    public string StatusCor => TemArquivo ? "#16A34A" : "#DC2626";
}

// ─────────────────────────────────────────────────────────────────────────────
// ViewModel
// ─────────────────────────────────────────────────────────────────────────────

public sealed partial class TemplateUpdateViewModel : ObservableObject
{
    // ── Dependências ──────────────────────────────────────────────────────
    private readonly ILabelRepository _repo;
    private readonly ILogger<TemplateUpdateViewModel> _logger;

    // Pasta base onde os templates ficam armazenados no computador.
    private readonly string _templateBaseDir =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "BuscaBT", "Templates");

    // ── Estado interno ────────────────────────────────────────────────────
    private List<TemplateItem> _allItems = [];

    // ── Propriedades observáveis ──────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<TemplateItem> _templates = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLabelCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveLabelCommand))]
    private TemplateItem? _selected;

    /// <summary>Texto de busca vinculado ao TextBox no header.</summary>
    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private string _statusText = "Pronto.";

    [ObservableProperty]
    private bool _isBusy;

    // ── Construtor ────────────────────────────────────────────────────────

    public TemplateUpdateViewModel(ILabelRepository repo, ILogger<TemplateUpdateViewModel> logger)
    {
        _repo = repo;
        _logger = logger;

        // Quando o usuário digitar no campo de busca, refiltrar a lista.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchTerm))
                ApplyFilter();
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // CARREGAR
    // ─────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        StatusText = "Carregando etiquetas…";

        try
        {
            var records = await _repo.GetAllTemplatesMasterAsync();

            _allItems = records
                .Select(r => new TemplateItem
                {
                    Id = r.Id,
                    Codigo = r.Codigo,
                    DescricaoAnvisa = r.DescricaoAnvisa,
                    LabelFilePath = r.LabelFilePath,
                    UpdatedAt = r.UpdatedAt
                })
                .ToList();

            ApplyFilter();
            StatusText = $"{_allItems.Count} etiqueta(s) carregada(s).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar etiquetas");
            StatusText = "Erro ao carregar etiquetas.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // FILTRO DE BUSCA
    // ─────────────────────────────────────────────────────────────────────

    private void ApplyFilter()
    {
        var term = SearchTerm.Trim();

        var filtered = string.IsNullOrWhiteSpace(term)
            ? _allItems
            : _allItems.Where(i =>
                i.Codigo.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                i.NomeArquivo.Contains(term, StringComparison.OrdinalIgnoreCase));

        Templates = new ObservableCollection<TemplateItem>(filtered);
    }

    // ─────────────────────────────────────────────────────────────────────
    // IMPORTAR PASTA  (associa .btw existentes sem mover arquivos)
    // ─────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        // Usando o componente nativo do WPF/Win32 para modernidade
        var dlg = new OpenFolderDialog
        {
            Title = "Selecione a pasta com os arquivos .btw",
            Multiselect = false
        };

        if (dlg.ShowDialog() != true) return;

        // Na nova API do Win32, a propriedade é FolderName
        await ProcessFolderAsync(dlg.FolderName, copyFiles: false);
    }

    // ─────────────────────────────────────────────────────────────────────
    // SINCRONIZAR NOVA PASTA
    // ─────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SyncNewFolderAsync()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Selecione a pasta de origem dos arquivos .btw",
            Multiselect = false
        };

        if (dlg.ShowDialog() != true) return;

        // Confirmar destino
        var destino = Path.Combine(_templateBaseDir,
            $"Sync_{DateTime.Now:yyyyMMdd_HHmmss}");

        var confirm = MessageBox.Show(
            $"Os arquivos .btw serão copiados para:\n\n{destino}\n\n" +
            $"Confirmar sincronização?",
            "Sincronizar Nova Pasta",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        await ProcessFolderAsync(dlg.FolderName, copyFiles: true, destDir: destino);
    }

    // Método central — importar OU sincronizar (com cópia)
    private async Task ProcessFolderAsync(string sourceDir, bool copyFiles, string? destDir = null)
    {
        IsBusy = true;
        StatusText = copyFiles ? "Sincronizando pasta…" : "Importando pasta…";

        try
        {
            var btwFiles = Directory.GetFiles(sourceDir, "*.btw", SearchOption.AllDirectories);

            if (btwFiles.Length == 0)
            {
                MessageBox.Show("Nenhum arquivo .btw encontrado na pasta selecionada.",
                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText = "Nenhum arquivo .btw encontrado.";
                return;
            }

            if (copyFiles && destDir is not null)
                Directory.CreateDirectory(destDir);

            // Monta lista (FileName sem extensão → caminho final)
            var templates = new List<(string FileName, string FilePath)>();

            foreach (var src in btwFiles)
            {
                var finalPath = src;

                if (copyFiles && destDir is not null)
                {
                    var dest = Path.Combine(destDir, Path.GetFileName(src));

                    // Preserva hierarquia de subpastas se houver
                    var rel = Path.GetRelativePath(sourceDir, src);
                    dest = Path.Combine(destDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                    File.Copy(src, dest, overwrite: true);
                    finalPath = dest;
                }

                // Chave de associação: nome do arquivo sem extensão = Codigo
                var codigo = Path.GetFileNameWithoutExtension(src);
                templates.Add((codigo, finalPath));
            }

            var atualizados = await _repo.UpsertTemplatesAsync(templates);

            StatusText = copyFiles
                ? $"Sincronização concluída. {atualizados} etiqueta(s) associada(s) na pasta:\n{destDir}"
                : $"{atualizados} etiqueta(s) associada(s) à pasta selecionada.";

            _logger.LogInformation(
                "ProcessFolder: {Count} templates processados, {Updated} atualizados. CopyFiles={Copy}",
                templates.Count, atualizados, copyFiles);

            await LoadAsync(); // atualiza a grid
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar pasta de templates");
            StatusText = $"Erro: {ex.Message}";
            MessageBox.Show($"Erro ao processar a pasta:\n{ex.Message}",
                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // IMPORTAR ARQUIVO ÚNICO
    // ─────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task AddSingleTemplateAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Selecionar arquivo de etiqueta",
            Filter = "BarTender Template (*.btw)|*.btw|Todos os arquivos (*.*)|*.*",
            Multiselect = false
        };

        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        StatusText = "Associando arquivo…";

        try
        {
            var codigo = Path.GetFileNameWithoutExtension(dlg.FileName);
            var count = await _repo.UpsertTemplatesAsync([(codigo, dlg.FileName)]);

            StatusText = count > 0
                ? $"Arquivo associado: {Path.GetFileName(dlg.FileName)}"
                : $"Nenhum registro encontrado com código '{codigo}'.";

            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao importar arquivo único");
            StatusText = $"Erro: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // ABRIR ETIQUETA no BarTender
    // ─────────────────────────────────────────────────────────────────────

    private bool CanOpenLabel() => Selected?.TemArquivo == true;

    [RelayCommand(CanExecute = nameof(CanOpenLabel))]
    private async Task OpenLabelAsync()
    {
        if (Selected?.LabelFilePath is not { } path) return;

        if (!File.Exists(path))
        {
            MessageBox.Show($"Arquivo não encontrado:\n{path}",
                "Arquivo não encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // Abre o .btw no programa associado (BarTender).
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true   // usa o programa padrão do sistema para .btw
            });

            StatusText = $"Abrindo: {Path.GetFileName(path)}";

            await Task.Delay(2000);
            StatusText = "Etiqueta aberta no BarTender. Salve diretamente no BarTender.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao abrir etiqueta {Path}", path);
            StatusText = $"Erro ao abrir etiqueta: {ex.Message}";
            MessageBox.Show($"Não foi possível abrir o arquivo:\n{ex.Message}",
                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // SALVAR / SUBSTITUIR arquivo de uma etiqueta selecionada
    // ─────────────────────────────────────────────────────────────────────

    private bool HasSelection() => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task SaveLabelAsync()
    {
        if (Selected is null) return;

        var dlg = new OpenFileDialog
        {
            Title = $"Substituir template: {Selected.Codigo}",
            Filter = "BarTender Template (*.btw)|*.btw|Todos os arquivos (*.*)|*.*",
            FileName = Path.GetFileName(Selected.LabelFilePath ?? string.Empty),
            Multiselect = false
        };

        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        StatusText = "Salvando associação…";

        try
        {
            // Copia para pasta centralizada se estiver fora dela
            var destPath = dlg.FileName;

            if (!dlg.FileName.StartsWith(_templateBaseDir, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(_templateBaseDir);
                destPath = Path.Combine(_templateBaseDir, Path.GetFileName(dlg.FileName));
                File.Copy(dlg.FileName, destPath, overwrite: true);
            }

            var ok = await _repo.AssociateLabelFileAsync(Selected.Id, destPath);

            StatusText = ok
                ? $"Template atualizado: {Path.GetFileName(destPath)}"
                : "Registro não encontrado no banco.";

            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao salvar template Id={Id}", Selected.Id);
            StatusText = $"Erro: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // FECHAR
    // ─────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private static void Close()
    {
        Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive)
            ?.Close();
    }
}