using System.Windows.Input;
using Busca_BT.Infrastructure;

namespace Busca_BT.ViewModels
{
    public sealed class HelpViewModel : BaseViewModel
    {
        private readonly INavigationService _navigation;

        public ICommand CloseCommand { get; }
        public ICommand OpenEmailCommand { get; }
        public ICommand OpenWhatsAppCommand { get; }
        public ICommand OpenGitHubCommand { get; }

        public HelpViewModel(INavigationService navigation)
        {
            _navigation = navigation;
            CloseCommand = new RelayCommand(() => _navigation.NavigateToNull());
            OpenEmailCommand = new RelayCommand(() => OpenEmail());
            OpenWhatsAppCommand = new RelayCommand(() => OpenWhatsApp());
            OpenGitHubCommand = new RelayCommand(() => OpenGitHub());
        }

        private void OpenEmail()
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo("mailto:theostracke11@gmail.com")
                    {
                        UseShellExecute = true
                    });
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao abrir email: {ex.Message}");
            }
        }

        private void OpenWhatsApp()
        {
            try
            {
                // WhatsApp Web URL format: https://wa.me/[country_code][number]?text=[message]
                // Brasil country code é 55
                var phoneNumber = "5548991027552";
                var message = "Olá, gostaria de solicitar suporte.";
                var encodedMessage = System.Uri.EscapeDataString(message);
                var url = $"https://wa.me/{phoneNumber}?text={encodedMessage}";
                
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(url)
                    {
                        UseShellExecute = true
                    });
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao abrir WhatsApp: {ex.Message}");
            }
        }

        private void OpenGitHub()
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo("https://github.com/theostracke")
                    {
                        UseShellExecute = true
                    });
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao abrir GitHub: {ex.Message}");
            }
        }
    }
}
