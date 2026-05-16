using Busca_BT.ViewModels;
using System.Windows.Controls;

namespace Busca_BT;

public partial class HelpView : UserControl
{
    public HelpView(HelpViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
