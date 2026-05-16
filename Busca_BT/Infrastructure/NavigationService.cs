using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;

namespace Busca_BT.Infrastructure
{
    public sealed class NavigationService : INavigationService
    {
        private readonly IServiceProvider _provider;
        private readonly ConcurrentDictionary<Type, Type> _map = new();

        public NavigationService(IServiceProvider provider)
        {
            _provider = provider;
        }

        public object? CurrentView { get; private set; }

        public event Action<object?>? CurrentViewChanged;

        public void MapsTo<TViewModel, TView>() where TView : class
        {
            _map[typeof(TViewModel)] = typeof(TView);
        }

        public void NavigateTo<TViewModel>() where TViewModel : class
        {
            if (!_map.TryGetValue(typeof(TViewModel), out var viewType))
                throw new InvalidOperationException($"No view mapped for {typeof(TViewModel).FullName}");

            var view = _provider.GetService(viewType) ?? ActivatorUtilities.CreateInstance(_provider, viewType);

            CurrentView = view;
            CurrentViewChanged?.Invoke(CurrentView);
        }

        public void NavigateToNull()
        {
            CurrentView = null;
            CurrentViewChanged?.Invoke(CurrentView);
        }
    }
}
