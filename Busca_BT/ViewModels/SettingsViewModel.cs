using Busca_BT.Data;
using Busca_BT.Infrastructure;
using Busca_BT.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Threading.Tasks;

namespace Busca_BT.ViewModels;

/// <summary>
/// Tela de Configurações: conexão com o banco na nuvem (Supabase / Postgres).
/// Fica salva por máquina em %AppData%\BuscaBT\supabase.settings.json, com a
/// senha criptografada para o usuário do Windows.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IConnectionSettingsStore _store;
    private readonly DatabaseOptions _dbOptions;
    private readonly DatabaseInitializer _initializer;
    private readonly Services.ISyncService _sync;
    private readonly IPreferenciasStore _preferencias;
    private readonly ILogger<SettingsViewModel> _logger;

    // ── Templates neste PC (Banco ou Pasta local) ─────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModoPasta))]
    private bool _modoBanco = true;

    public bool ModoPasta
    {
        get => !ModoBanco;
        set => ModoBanco = !value;
    }

    [ObservableProperty]
    private string _pastaTemplates = string.Empty;

    [ObservableProperty]
    private string _statusModo = string.Empty;

    [RelayCommand]
    private void EscolherPasta()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Pasta com os templates .btw" };
        if (dlg.ShowDialog() == true)
            PastaTemplates = dlg.FolderName;
    }

    [RelayCommand]
    private async Task AplicarModoAsync()
    {
        if (ModoPasta)
        {
            if (string.IsNullOrWhiteSpace(PastaTemplates) || !System.IO.Directory.Exists(PastaTemplates))
            {
                StatusModo = "Escolha uma pasta que exista neste PC ou na rede.";
                return;
            }
        }

        _preferencias.Salvar(new PreferenciasPc
        {
            ModoTemplates = ModoBanco ? ModoTemplates.Banco : ModoTemplates.PastaLocal,
            PastaTemplates = PastaTemplates.Trim()
        });

        if (ModoBanco)
        {
            StatusModo = "Modo Banco de dados ativado. Baixando o acervo para este PC…";
            var ok = await _sync.SincronizarAgoraAsync();
            StatusModo = ok
                ? "Modo Banco de dados ativado e acervo atualizado neste PC."
                : "Modo Banco de dados ativado. Sem internet agora: o acervo será baixado quando a conexão voltar.";
        }
        else
        {
            var qtd = System.IO.Directory.EnumerateFiles(PastaTemplates, "*.btw", System.IO.SearchOption.AllDirectories).Count();
            StatusModo = $"Modo Pasta local ativado: {qtd} arquivo(s) .btw encontrados.";
        }
    }

    [ObservableProperty]
    private string _host = string.Empty;

    [ObservableProperty]
    private string _port = "5432";

    [ObservableProperty]
    private string _database = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public SettingsViewModel(
        IConnectionSettingsStore store,
        DatabaseOptions dbOptions,
        DatabaseInitializer initializer,
        Services.ISyncService sync,
        IPreferenciasStore preferencias,
        ILogger<SettingsViewModel> logger)
    {
        _preferencias = preferencias;
        ModoBanco = preferencias.Atual.ModoTemplates == ModoTemplates.Banco;
        PastaTemplates = preferencias.Atual.PastaTemplates;

        _store = store;
        _dbOptions = dbOptions;
        _initializer = initializer;
        _sync = sync;
        _logger = logger;

        var current = _store.Load();
        Host = current.Host;
        Port = current.Port.ToString();
        Database = current.Database;
        Username = current.Username;
        // A senha da conexão padrão (embutida no .exe) não é mostrada na tela.
        Password = current.Embutida ? string.Empty : current.Password;
        if (current.Embutida)
            StatusText = "Usando a conexão padrão do PlusBT. Não é preciso preencher nada.";
    }

    private ConnectionSettings BuildSettings() => new()
    {
        Host = Host.Trim(),
        Port = int.TryParse(Port.Trim(), out var port) ? port : 5432,
        Database = Database.Trim(),
        Username = Username.Trim(),
        // Senha em branco com o usuário da conexão padrão: usa a senha embutida.
        Password = Password.Length == 0 && _store.Embutida is { } padrao
                   && string.Equals(padrao.Username, Username.Trim(), StringComparison.Ordinal)
            ? padrao.Password
            : Password
    };

    [RelayCommand]
    private async Task TestarConexaoAsync()
    {
        IsBusy = true;
        StatusText = "Testando conexão...";

        try
        {
            var settings = BuildSettings();
            if (!settings.IsComplete)
            {
                StatusText = "Preencha servidor, usuário e senha.";
                return;
            }

            await using var conn = new NpgsqlConnection(settings.BuildConnectionString());
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

            _dbOptions.ConnectionString = settings.IsComplete ? settings.BuildConnectionString() : string.Empty;

            await _initializer.InitializeAsync();
            _sync.MarcarBancoInicializado();
            await _sync.SincronizarAgoraAsync();
            StatusText = "Configuração salva. Conexão validada e dados sincronizados.";
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
