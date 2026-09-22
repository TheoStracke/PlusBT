using Busca_BT.Data;
using Busca_BT.Models;
using Busca_BT.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Busca_BT;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    public MainWindow(ViewModels.MainViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}