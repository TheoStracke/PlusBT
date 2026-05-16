using Busca_BT.Data;
using Busca_BT.Models;
using Busca_BT.Infrastructure;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;

namespace Busca_BT.ViewModels
{
    public sealed class HistoricoViewModel : BaseViewModel
    {
        private readonly ILabelRepository _repository;

        private int _page = 1;
        public int Page
        {
            get => _page;
            set
            {
                if (SetProperty(ref _page, value))
                {
                    RaisePropertyChanged(nameof(PageDisplay));
                    CommandManager.InvalidateRequerySuggested();
                    _ = LoadPageAsync();
                }
            }
        }

        private int _pageSize = 50;
        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (SetProperty(ref _pageSize, value))
                {
                    RaisePropertyChanged(nameof(TotalPages));
                    RaisePropertyChanged(nameof(PageDisplay));
                    CommandManager.InvalidateRequerySuggested();
                    _ = LoadPageAsync();
                }
            }
        }

        private int _totalRecords;
        public int TotalRecords
        {
            get => _totalRecords;
            private set
            {
                if (SetProperty(ref _totalRecords, value))
                {
                    RaisePropertyChanged(nameof(TotalPages));
                    RaisePropertyChanged(nameof(PageDisplay));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalRecords / (double)PageSize);

        public string PageDisplay => $"Página {Page} de {TotalPages}";

        public ObservableCollection<ImportBatchRecord> Batches { get; } = new();

        public ICommand NextPageCommand { get; private set; }
        public ICommand PreviousPageCommand { get; private set; }
        public ICommand DeleteBatchCommand { get; private set; }
        public ICommand CloseCommand { get; }

        private ImportBatchRecord[] _all = [];
        private readonly INavigationService _navigation;
        private readonly IDialogService _dialog;

        public HistoricoViewModel(ILabelRepository repository, INavigationService navigation, IDialogService dialog)
        {
            _repository = repository;
            _navigation = navigation;
            _dialog = dialog;

            PreviousPageCommand = new RelayCommand(() => Page--, () => Page > 1);
            NextPageCommand = new RelayCommand(() => Page++, () => Page < TotalPages);

            DeleteBatchCommand = new RelayCommand(async p => await DeleteBatchAsync(p), p => p is ImportBatchRecord);
            CloseCommand = new RelayCommand(() => _navigation.NavigateToNull());
        }

        public async Task LoadAllAsync()
        {
            var batches = (await _repository.GetBatchesAsync()).ToArray();
            _all = batches;
            TotalRecords = _all.Length;
            Page = 1;
            await LoadPageAsync();
        }

        public Task LoadPageAsync()
        {
            Batches.Clear();

            var skip = (Page - 1) * PageSize;
            var pageItems = _all.Skip(skip).Take(PageSize).ToArray();

            foreach (var b in pageItems)
                Batches.Add(b);

            CommandManager.InvalidateRequerySuggested();
            return Task.CompletedTask;
        }

        private async Task DeleteBatchAsync(object? param)
        {
            if (param is not ImportBatchRecord batch) return;

            var confirm = _dialog.AskConfirmation("Confirmar exclusão",
                $"Excluir a importação:\n\n{batch.FileName}\n{batch.ImportedAt:dd/MM/yyyy HH:mm}\n\n" +
                $"Isso removerá {batch.ImportedRows} registros do banco.");

            if (!confirm) return;

            await _repository.DeleteBatchAsync(batch.Id);
            await LoadAllAsync();

            _dialog.ShowInfo("Exclusão", "Importação excluída com sucesso.");
        }
    }
}
