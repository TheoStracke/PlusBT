using Busca_BT.ViewModels;
using System.Windows.Controls;

namespace Busca_BT;

public partial class OperadoresView : UserControl
{
    public OperadoresView(OperadoresViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.CarregarAsync();

        // PasswordBox não tem binding: repassa o PIN e limpa a caixa quando o VM limpar.
        PinBox.PasswordChanged += (_, _) => viewModel.NovoPin = PinBox.Password;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OperadoresViewModel.NovoPin) && viewModel.NovoPin.Length == 0 && PinBox.Password.Length > 0)
                PinBox.Password = string.Empty;
        };
    }
}
