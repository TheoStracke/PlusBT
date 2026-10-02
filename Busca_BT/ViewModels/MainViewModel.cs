using Busca_BT.Infrastructure;
using Busca_BT.Services;
using System.Windows.Input;

namespace Busca_BT.ViewModels
{
    public sealed class MainViewModel : BaseViewModel
    {
        private readonly INavigationService _navigation;
        private readonly ISessaoOperador _sessao;
        private readonly IEventoService _eventos;

        public object? CurrentView { get; private set; }

        /// <summary>Tela "Quem está operando?" (fica por cima de tudo até alguém entrar).</summary>
        public OperadorSelecaoViewModel Selecao { get; }

        public ICommand ShowTemplatesCommand { get; }
        public ICommand ShowHistoricoCommand { get; }
        public ICommand ShowHomeCommand { get; }
        public ICommand ShowHelpCommand { get; }
        public ICommand ShowSettingsCommand { get; }
        public ICommand ShowOperadoresCommand { get; }
        public ICommand TrocarOperadorCommand { get; }

        public MainViewModel(
            INavigationService navigation,
            ISessaoOperador sessao,
            IEventoService eventos,
            OperadorSelecaoViewModel selecao)
        {
            _navigation = navigation;
            _sessao = sessao;
            _eventos = eventos;
            Selecao = selecao;

            _navigation.CurrentViewChanged += OnCurrentViewChanged;
            _sessao.Mudou += OnSessaoMudou;
            Selecao.AbrirConfiguracoesSolicitado += () => _navigation.NavigateTo<SettingsViewModel>();

            ShowHomeCommand = new RelayCommand(() => _navigation.NavigateTo<HomeViewModel>());
            ShowTemplatesCommand = new RelayCommand(() => _navigation.NavigateTo<TemplateUpdateViewModel>(), () => IsAdmin);
            ShowHistoricoCommand = new RelayCommand(() => _navigation.NavigateTo<HistoricoViewModel>());
            ShowHelpCommand = new RelayCommand(() => _navigation.NavigateTo<HelpViewModel>());
            ShowSettingsCommand = new RelayCommand(() => _navigation.NavigateTo<SettingsViewModel>(), () => PodeConfigurar);
            ShowOperadoresCommand = new RelayCommand(() => _navigation.NavigateTo<OperadoresViewModel>(), () => IsAdmin);
            TrocarOperadorCommand = new RelayCommand(async () => await TrocarOperadorAsync());

            // Nenhuma tela por trás até alguém entrar: a Home é aberta depois do login.
            _ = Selecao.CarregarAsync();
        }

        // ── Operador atual ────────────────────────────────────────────────

        public bool TemOperador => _sessao.Atual is not null;
        public bool IsAdmin => _sessao.IsAdmin;
        public string OperadorNome => _sessao.Atual?.Nome ?? string.Empty;
        public string OperadorIniciais => _sessao.Atual?.Iniciais ?? string.Empty;
        public string OperadorPerfil => _sessao.Atual?.PerfilTexto ?? string.Empty;

        /// <summary>Conexão com o banco: só administrador (ou antes de alguém entrar, para configurar o PC).</summary>
        public bool PodeConfigurar => IsAdmin || !TemOperador;

        /// <summary>
        /// A seleção cobre o app enquanto ninguém entrou, exceto na tela de Configurações
        /// (sem conexão com o banco não há operadores para escolher).
        /// </summary>
        public bool IsSelecaoVisible => !TemOperador && CurrentView is not SettingsView;

        private void OnSessaoMudou()
        {
            RaisePropertyChanged(nameof(TemOperador));
            RaisePropertyChanged(nameof(IsAdmin));
            RaisePropertyChanged(nameof(OperadorNome));
            RaisePropertyChanged(nameof(OperadorIniciais));
            RaisePropertyChanged(nameof(OperadorPerfil));
            RaisePropertyChanged(nameof(PodeConfigurar));
            RaisePropertyChanged(nameof(IsSelecaoVisible));
            CommandManager.InvalidateRequerySuggested();

            if (TemOperador)
            {
                // Telas recriadas com as permissões de quem entrou.
                _navigation.NavigateTo<HomeViewModel>();
            }
            else
            {
                _navigation.NavigateToNull();
                _ = Selecao.CarregarAsync();
            }
        }

        private async Task TrocarOperadorAsync()
        {
            if (!TemOperador)
                return;

            await _eventos.RegistrarAsync(Acoes.Saiu);
            _sessao.Sair();
        }

        private void OnCurrentViewChanged(object? view)
        {
            var saiuDasConfiguracoes = CurrentView is SettingsView && view is not SettingsView;

            CurrentView = view;
            RaisePropertyChanged(nameof(CurrentView));
            RaisePropertyChanged(nameof(IsSelecaoVisible));

            // Voltando das Configurações sem ninguém logado: tenta carregar os operadores de novo.
            if (saiuDasConfiguracoes && !TemOperador)
                _ = Selecao.CarregarAsync();
        }
    }
}
