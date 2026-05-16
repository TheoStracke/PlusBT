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
        }

        private async void Application_Startup(object sender, StartupEventArgs e)
        {
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

                    // Register ViewModels
                    services.AddSingleton<MainViewModel>();
                    services.AddTransient<TemplateUpdateViewModel>();
                    services.AddTransient<HistoricoViewModel>();
                    services.AddTransient<HomeViewModel>();
                    services.AddTransient<HelpViewModel>();

                    services.AddTransient<MainWindow>();
                })
                .Build();

            await AppStartup.InitializeDatabaseAsync(_host.Services);

            ServiceProvider = _host.Services;

            // Configure navigation maps
            var nav = ServiceProvider.GetRequiredService<INavigationService>();
            nav.MapsTo<TemplateUpdateViewModel, TemplateUpdateWindow>();
            nav.MapsTo<HistoricoViewModel, HistoricoWindow>();
            nav.MapsTo<HomeViewModel, HomeView>();
            nav.MapsTo<HelpViewModel, HelpView>();

            // Register converters in Application resources so XAML can reference by key
            Current.Resources["NullToVisibilityConverter"] = new NullToVisibilityConverter();
            Current.Resources["NotNullToBoolConverter"] = new NotNullToBoolConverter();
            Current.Resources["StringNotEmptyToBoolConverter"] = new StringNotEmptyToBoolConverter();

            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
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
