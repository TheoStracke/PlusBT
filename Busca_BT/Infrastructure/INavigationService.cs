using System;

namespace Busca_BT.Infrastructure
{
    /// <summary>
    /// Serviço simples de navegação: mapeia ViewModel -> View e permite navegar para uma View
    /// A implementação mantém o CurrentView (objeto) e dispara evento quando muda.
    /// </summary>
    public interface INavigationService
    {
        /// <summary>
        /// Registra mapeamento entre ViewModel e View (tipos).
        /// </summary>
        void MapsTo<TViewModel, TView>() where TView : class;

        /// <summary>
        /// Navega para a View associada ao ViewModel TViewModel.
        /// </summary>
        void NavigateTo<TViewModel>() where TViewModel : class;

        /// <summary>
        /// Limpa a view atual (volta para estado sem view no ContentControl).
        /// </summary>
        void NavigateToNull();

        /// <summary>
        /// Objeto atualmente exibido (View resolveda pelo DI).
        /// </summary>
        object? CurrentView { get; }

        /// <summary>
        /// Evento disparado quando CurrentView muda.
        /// </summary>
        event Action<object?>? CurrentViewChanged;
    }
}
