using System.Windows;
using System.Windows.Media.Animation;

namespace Busca_BT;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();

        // Mesmo lugar da splash nativa (centro do monitor principal), para a troca
        // entre as duas ser imperceptível. CenterScreen do WPF pode escolher outro monitor.
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
    }

    public void SetStatus(string text) => StatusText.Text = text;

    /// <summary>Some com fade e fecha a janela.</summary>
    public Task CloseWithFadeAsync()
    {
        var tcs = new TaskCompletionSource();
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) =>
        {
            Close();
            tcs.TrySetResult();
        };
        BeginAnimation(OpacityProperty, fade);
        return tcs.Task;
    }
}
