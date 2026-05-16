namespace Busca_BT.Infrastructure
{
    public interface IDialogService
    {
        void ShowInfo(string title, string message);
        void ShowWarning(string title, string message);
        void ShowError(string title, string message);
        bool AskConfirmation(string title, string message);
        bool ShowQuestion(string title, string message);
    }
}
