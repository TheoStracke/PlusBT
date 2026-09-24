using Busca_BT.Data;
using Busca_BT.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Busca_BT.Infrastructure
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddLabelSystem(this IServiceCollection services)
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

            services.AddSingleton<IDbConnectionFactory, SqlServerConnectionFactory>();
            services.AddSingleton<ISqlServerDiscoveryService, SqlServerDiscoveryService>();
            services.AddSingleton<DatabaseInitializer>();

            services.AddSingleton<ILabelRepository, LabelRepository>();
            services.AddSingleton<IExcelImportService, ExcelImportService>();
            services.AddSingleton<IInvoiceReportService, InvoiceReportService>();

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
