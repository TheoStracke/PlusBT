using Busca_BT.ViewModels;
using System.Windows.Controls;

namespace Busca_BT;

public partial class HomeView : UserControl
{
    public HomeView(HomeViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }
}
