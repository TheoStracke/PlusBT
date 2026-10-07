using Velopack;

namespace Busca_BT;

/// <summary>
/// Ponto de entrada próprio (em vez do Main gerado pelo App.xaml): o Velopack precisa
/// rodar antes de qualquer janela. Na instalação, atualização e desinstalação ele trata
/// o evento e encerra o processo; se houver uma atualização já baixada, aplica aqui.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
