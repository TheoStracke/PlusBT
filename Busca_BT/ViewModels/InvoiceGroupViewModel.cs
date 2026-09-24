using Busca_BT.Infrastructure;
using Busca_BT.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace Busca_BT.ViewModels
{
    /// <summary>
    /// Uma linha da Home: todas as etiquetas de uma mesma invoice, com as folhas de
    /// espelho e o progresso (quantas já foram abertas) calculados só para ela.
    /// </summary>
    public sealed class InvoiceGroupViewModel : BaseViewModel
    {
        private readonly IReadOnlyList<LabelRecord> _all;

        public InvoiceGroupViewModel(string invoice, IReadOnlyList<LabelRecord> labels)
        {
            Invoice = invoice;
            _all = labels;
            TotalEtiquetas = labels.Sum(l => l.QtdInvoice);

            // Quando um rótulo vira "Aberto", a barra de progresso é recalculada na hora.
            foreach (var label in _all)
                label.PropertyChanged += OnLabelChanged;

            ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
            ApplyFilter(string.Empty);
        }

        public string Invoice { get; }

        /// <summary>Rótulos visíveis (filtrados pela busca da Home).</summary>
        public ObservableCollection<LabelRecord> Items { get; } = new();

        public int Total => _all.Count;
        /// <summary>Etiquetas físicas a imprimir: soma da Qtd Invoice dos itens.</summary>
        public int TotalEtiquetas { get; }
        public int Abertas => _all.Count(l => l.FoiAberta);

        public double Progresso => Total == 0 ? 0 : Abertas * 100.0 / Total;
        public bool Concluida => Total > 0 && Abertas == Total;

        public string ProgressoTexto => $"{Abertas}/{Total}";
        public string ResumoTexto => $"{Contagem.Itens(Total)}  ·  {Contagem.Etiquetas(TotalEtiquetas)}";

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public ICommand ToggleCommand { get; }

        /// <summary>
        /// Filtra os rótulos exibidos. Retorna false quando nenhum rótulo bate com o
        /// termo (a Home esconde a invoice). O progresso e as folhas não mudam com a busca.
        /// </summary>
        public bool ApplyFilter(string termo)
        {
            var semTermo = string.IsNullOrEmpty(termo);
            var invoiceBate = !semTermo && Invoice.Contains(termo, StringComparison.OrdinalIgnoreCase);

            var visiveis = semTermo || invoiceBate
                ? _all
                : _all.Where(l =>
                    l.Codigo.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                    l.Lote.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                    l.Lpn.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                    l.DescricaoAnvisa.Contains(termo, StringComparison.OrdinalIgnoreCase)).ToList();

            Items.Clear();
            foreach (var l in visiveis)
                Items.Add(l);

            return Items.Count > 0;
        }

        private void OnLabelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LabelRecord.FoiAberta))
                return;

            RaisePropertyChanged(nameof(Abertas));
            RaisePropertyChanged(nameof(Progresso));
            RaisePropertyChanged(nameof(ProgressoTexto));
            RaisePropertyChanged(nameof(Concluida));
        }
    }
}
