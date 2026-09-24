using Busca_BT.Infrastructure;
using Busca_BT.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
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

        // Imagem estática mostrada no instante do clique no .exe (antes do WPF carregar
        // a primeira janela). Fecha quando a SplashWindow animada termina de desenhar.
        private readonly SplashScreen _nativeSplash = new("splash.png");

        public App()
        {
            _nativeSplash.Show(autoClose: false, topMost: true);

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
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
                Wpf.Ui.Appearance.ApplicationTheme.Light,
                Wpf.Ui.Controls.WindowBackdropType.Mica);

            // Tela de abertura: primeira coisa que aparece ao clicar no .exe.
            var splash = new SplashWindow();
            splash.ContentRendered += (_, _) => _nativeSplash.Close(TimeSpan.FromMilliseconds(150));
            splash.Show();

            try
            {
                await RunStartupAsync(e, splash);
            }
            catch (Exception ex)
            {
                _nativeSplash.Close(TimeSpan.Zero);
                splash.Close();
                MessageBox.Show(
                    $"Não foi possível iniciar o aplicativo:\n\n{ex.Message}",
                    "Erro ao iniciar", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
            }
        }

        private async Task RunStartupAsync(StartupEventArgs e, SplashWindow splash)
        {
            // Montagem do host e conexão com o banco rodam fora da thread da UI,
            // para a animação da tela de abertura não travar.
            _host = await Task.Run(() => BuildHost(e.Args));
            ServiceProvider = _host.Services;

            splash.SetStatus("Conectando ao banco de dados…");
            var dbReady = await Task.Run(TryInitializeDatabaseAsync);

            splash.SetStatus("Carregando invoices…");

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
            MainWindow = mainWindow; // a splash foi a primeira janela; a principal é esta

            // Só tira a splash depois que a janela principal terminou de desenhar.
            var rendered = new TaskCompletionSource();
            mainWindow.ContentRendered += (_, _) => rendered.TrySetResult();
            mainWindow.Show();
            await rendered.Task;
            await splash.CloseWithFadeAsync();

            if (!dbReady)
            {
                nav.NavigateTo<SettingsViewModel>();
                MessageBox.Show(
                    "Não foi possível conectar ao banco de dados configurado.\n\n" +
                    "Ajuste o servidor na aba Configurações e clique em Salvar.",
                    "Conexão indisponível", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static IHost BuildHost(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    // appsettings.json embutido no .exe (LogicalName definido no .csproj)
                    // como base — o app funciona mesmo sem nenhum arquivo solto ao lado
                    // do executável. Inserido no início da lista de fontes para que um
                    // appsettings.json externo (se alguém colocar um do lado do .exe)
                    // continue tendo prioridade e sobrescrevendo os valores embutidos.
                    var assembly = Assembly.GetExecutingAssembly();
                    using var resourceStream = assembly.GetManifestResourceStream("Busca_BT.appsettings.json");
                    if (resourceStream is not null)
                    {
                        var buffer = new MemoryStream();
                        resourceStream.CopyTo(buffer);
                        buffer.Position = 0;
                        config.Sources.Insert(0, new JsonStreamConfigurationSource { Stream = buffer });
                    }
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddLogging(logging =>
                    {
                        logging.ClearProviders();
                        logging.AddConsole();
                        logging.SetMinimumLevel(LogLevel.Information);
                    });

                    services.AddLabelSystem();

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
