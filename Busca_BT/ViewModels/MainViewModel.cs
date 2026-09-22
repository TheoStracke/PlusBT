using Busca_BT.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows.Input;

namespace Busca_BT.ViewModels
{
    public sealed class MainViewModel : BaseViewModel
    {
        private readonly INavigationService _navigation;
        public object? CurrentView { get; private set; }

        public ICommand ShowTemplatesCommand { get; }
        public ICommand ShowHistoricoCommand { get; }
        public ICommand ShowHomeCommand { get; }
        public ICommand ShowHelpCommand { get; }
        public ICommand ShowSettingsCommand { get; }

        public MainViewModel(INavigationService navigation)
        {
            _navigation = navigation;
            _navigation.CurrentViewChanged += OnCurrentViewChanged;

            ShowHomeCommand = new Infrastructure.RelayCommand(() => _navigation.NavigateTo<HomeViewModel>());
            ShowTemplatesCommand = new Infrastructure.RelayCommand(() => _navigation.NavigateTo<TemplateUpdateViewModel>());
            ShowHistoricoCommand = new Infrastructure.RelayCommand(() => _navigation.NavigateTo<HistoricoViewModel>());
            ShowHelpCommand = new Infrastructure.RelayCommand(() => _navigation.NavigateTo<HelpViewModel>());
            ShowSettingsCommand = new Infrastructure.RelayCommand(() => _navigation.NavigateTo<SettingsViewModel>());

            // Start with Home
            _navigation.NavigateTo<HomeViewModel>();
        }

        private void OnCurrentViewChanged(object? view)
        {
            CurrentView = view;
            RaisePropertyChanged(nameof(CurrentView));
        }
    }
}
