using Busca_BT.Data;
using Busca_BT.Models;
using Busca_BT.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Busca_BT.Infrastructure
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddLabelSystem(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddSingleton<IConnectionSettingsStore, ConnectionSettingsStore>();

            services.AddSingleton(sp =>
            {
                var settings = sp.GetRequiredService<IConnectionSettingsStore>().Load();
                var connectionString = settings.BuildConnectionString();
                return new DatabaseOptions
                {
                    ConnectionString = connectionString,
                    ActiveConnectionString = connectionString,
                    AutoDiscover = settings.AutoDiscover
                };
            });

            services.AddSingleton(new EtiquetasOptions
            {
                PastaArquivos = configuration[$"{EtiquetasOptions.Section}:PastaArquivos"] ?? string.Empty
            });

            services.Configure<BartenderOptions>(
                configuration.GetSection(BartenderOptions.Section));

            services.AddSingleton<IDbConnectionFactory, SqlServerConnectionFactory>();
            services.AddSingleton<ISqlServerDiscoveryService, SqlServerDiscoveryService>();
            services.AddSingleton<DatabaseInitializer>();

            services.AddSingleton<ILabelRepository, LabelRepository>();
            services.AddSingleton<IExcelImportService, ExcelImportService>();
            services.AddSingleton<IFileUpdateService, FileUpdateService>();
            services.AddSingleton<IBartenderService, BartenderService>();
            services.AddSingleton<IBartenderLocator, BartenderLocator>();

            return services;
        }
    }

    public static class AppStartup
    {
        /// <summary>
        /// Inicializa a conexão com o banco (valida as tabelas obrigatórias).
        /// Chame antes de exibir a janela principal.
        /// </summary>
        public static async Task InitializeDatabaseAsync(IServiceProvider services)
        {
            var initializer = services.GetRequiredService<DatabaseInitializer>();
            await initializer.InitializeAsync();
        }
    }
}

/*
─────────────────────────────────────────────────────────────────────────────
 App.xaml.cs  (WPF — exemplo de bootstrap completo)
─────────────────────────────────────────────────────────────────────────────

protected override async void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);

    _host = Host.CreateDefaultBuilder()
        .ConfigureAppConfiguration(cfg =>
            cfg.AddJsonFile("appsettings.json"))
        .ConfigureServices((ctx, services) =>
        {
            services.AddLabelSystem(ctx.Configuration);
            services.AddTransient<MainWindow>();
            services.AddTransient<MainViewModel>();
        })
        .Build();

    await AppStartup.InitializeDatabaseAsync(_host.Services);
    _host.Start();

    _host.Services.GetRequiredService<MainWindow>().Show();
}

─────────────────────────────────────────────────────────────────────────────
 appsettings.json
─────────────────────────────────────────────────────────────────────────────

{
  "Database": {
    "ConnectionString": "Data Source=%APPDATA%\\Busca_BT\\labels.db"
  },
  "Bartender": {
    "ExecutablePath": "C:\\Program Files (x86)\\Seagull\\BarTender 2016\\bartend.exe",
    "StartupTimeoutMs": 3000,
    "SilentPrint": false
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Busca_BT": "Debug"
    }
  }
}
*/