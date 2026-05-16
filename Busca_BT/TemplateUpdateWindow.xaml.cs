using Busca_BT.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Busca_BT;

public partial class TemplateUpdateWindow : UserControl
{
    private TemplateUpdateViewModel _vm = null!;

    public TemplateUpdateWindow(TemplateUpdateViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _vm = viewModel;
        Loaded += (_, _) => _vm.LoadCommand.Execute(null);
    }

    // Duplo clique em uma linha → abre a etiqueta diretamente
    private void GridTemplates_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.OpenLabelCommand.CanExecute(null))
            _vm.OpenLabelCommand.Execute(null);
    }

    // Botão "Abrir" inline na coluna — seleciona a linha e dispara o comando
    private void BtnAbrirRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ViewModels.TemplateItem item })
        {
            _vm.Selected = item;
            if (_vm.OpenLabelCommand.CanExecute(null))
                _vm.OpenLabelCommand.Execute(null);
        }
    }
}
