using Busca_BT.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace Busca_BT;

public partial class TemplateUpdateWindow : UserControl
{
    private readonly TemplateUpdateViewModel _vm;

    public TemplateUpdateWindow(TemplateUpdateViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _vm = viewModel;

        Loaded += (_, _) =>
        {
            _vm.Ativar();
            _vm.Carregar();
        };
        Unloaded += (_, _) => _vm.Desativar();
    }

    // Duplo clique em uma linha → abre o template no BarTender
    private void GridTemplates_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.AbrirCommand.CanExecute(null))
            _vm.AbrirCommand.Execute(null);
    }
}
