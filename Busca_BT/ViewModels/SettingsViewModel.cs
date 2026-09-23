using Busca_BT.Data;
using Busca_BT.Infrastructure;
using Busca_BT.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Busca_BT.ViewModels;

/// <summary>
/// Tela de Configurações: permite trocar servidor/instância/credenciais do SQL Server
/// sem precisar editar appsettings.json e recompilar o app. Fica salvo por máquina em
/// %AppData%\BuscaBT\connection.settings.json.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IConnectionSettingsStore _store;
    private readonly DatabaseOptions _dbOptions;
    private readonly DatabaseInitializer _initializer;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty]
    private string _server = string.Empty;

    [ObservableProperty]
    private string _instance = string.Empty;

    [ObservableProperty]
    private string _database = string.Empty;

    [ObservableProperty]
    private bool _useWindowsAuth = true;

    [ObservableProperty]
    private string _userId = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _autoDiscover = true;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public SettingsViewModel(
        IConnectionSettingsStore store,
        DatabaseOptions dbOptions,
        DatabaseInitializer initializer,
        ILogger<SettingsViewModel> logger)
    {
        _store = store;
        _dbOptions = dbOptions;
        _initializer = initializer;
        _logger = logger;

        var current = _store.Load();
        Server = current.Server;
        Instance = current.Instance;
        Database = current.Database;
        UseWindowsAuth = current.UseWindowsAuth;
        UserId = current.UserId;
        Password = current.Password;
        AutoDiscover = current.AutoDiscover;
    }

    private ConnectionSettings BuildSettings() => new()
    {
        Server = Server.Trim(),
        Instance = Instance.Trim(),
        Database = Database.Trim(),
        UseWindowsAuth = UseWindowsAuth,
        UserId = UserId.Trim(),
        Password = Password,
        AutoDiscover = AutoDiscover,
        TrustServerCertificate = true,
        ConnectTimeoutSeconds = 10
    };

    [RelayCommand]
    private async Task TestarConexaoAsync()
    {
        IsBusy = true;
        StatusText = "Testando conexão...";

        try
        {
            var connectionString = BuildSettings().BuildConnectionString();
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            StatusText = "Conexão bem-sucedida.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Teste de conexão falhou");
            StatusText = $"Falha na conexão: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SalvarAsync()
    {
        IsBusy = true;
        StatusText = "Salvando...";

        try
        {
            var settings = BuildSettings();
            _store.Save(settings);

            var connectionString = settings.BuildConnectionString();
            _dbOptions.ConnectionString = connectionString;
            _dbOptions.ActiveConnectionString = connectionString;
            _dbOptions.AutoDiscover = settings.AutoDiscover;

            await _initializer.InitializeAsync();
            StatusText = "Configuração salva. Conexão validada com sucesso.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao validar conexão após salvar configurações");
            StatusText = $"Configuração salva, mas não foi possível conectar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
