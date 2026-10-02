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
                return new DatabaseOptions
                {
                    // Vazio quando ainda não há senha: o app abre direto em Configurações.
                    ConnectionString = settings.IsComplete ? settings.BuildConnectionString() : string.Empty
                };
            });

            services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
            services.AddSingleton<DatabaseInitializer>();

            services.AddSingleton<ISessaoOperador, SessaoOperador>();

            // Supabase (acesso direto) + cópia local; as telas usam as versões "Offline",
            // que leem da cópia local e só exigem internet nas ações de administrador.
            services.AddSingleton<LabelRepository>();
            services.AddSingleton<OperadorRepository>();
            services.AddSingleton<LocalCache>();
            services.AddSingleton<ISyncService, SyncService>();
            services.AddSingleton<ILabelRepository, OfflineLabelRepository>();
            services.AddSingleton<IOperadorRepository, OfflineOperadorRepository>();
            services.AddSingleton<IEventoService, EventoService>();
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
