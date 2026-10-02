using Busca_BT.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Busca_BT;

public partial class OperadorSelecaoView : UserControl
{
    public OperadorSelecaoView()
    {
        InitializeComponent();

        // PasswordBox não tem binding: repassa o PIN digitado para o ViewModel.
        PinBox.PasswordChanged += (_, _) =>
        {
            if (DataContext is OperadorSelecaoViewModel vm)
                vm.Pin = PinBox.Password;
        };

        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged antigo)
                antigo.PropertyChanged -= OnViewModelChanged;
            if (e.NewValue is INotifyPropertyChanged novo)
                novo.PropertyChanged += OnViewModelChanged;
        };
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not OperadorSelecaoViewModel vm)
            return;

        // PIN limpo pelo ViewModel (erro, voltar) → limpa a caixa.
        if (e.PropertyName == nameof(OperadorSelecaoViewModel.Pin) && vm.Pin.Length == 0 && PinBox.Password.Length > 0)
            PinBox.Password = string.Empty;

        // Ao pedir o PIN, já deixa o cursor na caixa.
        if (e.PropertyName == nameof(OperadorSelecaoViewModel.PedindoPin) && vm.PedindoPin)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => PinBox.Focus());
    }
}
