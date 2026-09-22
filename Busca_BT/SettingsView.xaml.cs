using Busca_BT.ViewModels;
using System.Windows.Controls;

namespace Busca_BT;

public partial class SettingsView : UserControl
{
    public SettingsView(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        PasswordBox.Password = viewModel.Password;
        PasswordBox.PasswordChanged += (_, _) => viewModel.Password = PasswordBox.Password;
    }
}
