using Busca_BT.Data;
using Busca_BT.Models;
using Busca_BT.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Busca_BT;

public partial class HistoricoWindow : UserControl
{
    public HistoricoWindow(HistoricoViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAllAsync();
    }
}