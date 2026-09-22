using Busca_BT.Infrastructure;
using Busca_BT.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Windows;
using Busca_BT.Infrastructure.Converters;

namespace Busca_BT
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private IHost? _host;

        public static IServiceProvider ServiceProvider { get; private set; } = null!;

        public App()
        {
            Startup += Application_Startup;
            Exit += Application_Exit;
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += AppDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                $"Ocorreu um erro inesperado:\n\n{e.Exception.Message}",
                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void AppDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
                MessageBox.Show(
                    $"Ocorreu um erro fatal:\n\n{ex.Message}",
                    "Erro fatal", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
        }

        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                await RunStartupAsync(e);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Não foi possível iniciar o aplicativo:\n\n{ex.Message}",
                    "Erro ao iniciar", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
            }
        }

        private async Task RunStartupAsync(StartupEventArgs e)
        {
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
                Wpf.Ui.Appearance.ApplicationTheme.Light,
                Wpf.Ui.Controls.WindowBackdropType.Mica);

            _host = Host.CreateDefaultBuilder(e.Args)
                .ConfigureServices((context, services) =>
                {
                    services.AddLogging(logging =>
                    {
                        logging.ClearProviders();
                        logging.AddConsole();
                        logging.SetMinimumLevel(LogLevel.Information);
                    });

                    services.AddLabelSystem(context.Configuration);

                    // Navigation infrastructure
                    services.AddSingleton<INavigationService, NavigationService>();

                    // Dialog service
                    services.AddSingleton<IDialogService, WpfDialogService>();

                    // Register Views
                    services.AddTransient<TemplateUpdateWindow>();
                    services.AddTransient<HistoricoWindow>();
                    services.AddTransient<HomeView>();
                    services.AddTransient<HelpView>();
                    services.AddTransient<SettingsView>();

                    // Register ViewModels
                    services.AddSingleton<MainViewModel>();
                    services.AddTransient<TemplateUpdateViewModel>();
                    services.AddTransient<HistoricoViewModel>();
                    services.AddTransient<HomeViewModel>();
                    services.AddTransient<HelpViewModel>();
                    services.AddTransient<SettingsViewModel>();

                    services.AddTransient<MainWindow>();
                })
                .Build();

            ServiceProvider = _host.Services;

            var dbReady = await TryInitializeDatabaseAsync();

            // Configure navigation maps
            var nav = ServiceProvider.GetRequiredService<INavigationService>();
            nav.MapsTo<TemplateUpdateViewModel, TemplateUpdateWindow>();
            nav.MapsTo<HistoricoViewModel, HistoricoWindow>();
            nav.MapsTo<HomeViewModel, HomeView>();
            nav.MapsTo<HelpViewModel, HelpView>();
            nav.MapsTo<SettingsViewModel, SettingsView>();

            // Register converters in Application resources so XAML can reference by key
            Current.Resources["NullToVisibilityConverter"] = new NullToVisibilityConverter();
            Current.Resources["NotNullToBoolConverter"] = new NotNullToBoolConverter();
            Current.Resources["StringNotEmptyToBoolConverter"] = new StringNotEmptyToBoolConverter();

            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();

            if (!dbReady)
            {
                nav.NavigateTo<SettingsViewModel>();
                MessageBox.Show(
                    "Não foi possível conectar ao banco de dados configurado.\n\n" +
                    "Ajuste o servidor na aba Configurações e clique em Salvar.",
                    "Conexão indisponível", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task<bool> TryInitializeDatabaseAsync()
        {
            try
            {
                await AppStartup.InitializeDatabaseAsync(ServiceProvider);
                return true;
            }
            catch (Exception ex)
            {
                ServiceProvider.GetRequiredService<ILogger<App>>()
                    .LogCritical(ex, "Falha ao conectar no banco de dados durante o startup.");
                return false;
            }
        }

        private async void Application_Exit(object sender, ExitEventArgs e)
        {
            if (_host is not null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
        }
    }
}
