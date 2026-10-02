namespace Busca_BT.ViewModels
{
    /// <summary>
    /// Uma pendência do rótulo no modal de ciência: o usuário marca a caixa para
    /// confirmar que viu o problema antes de abrir/imprimir mesmo assim.
    /// </summary>
    public sealed class CienciaItem(string texto) : BaseViewModel
    {
        public string Texto { get; } = texto;

        private bool _ciente;
        public bool Ciente
        {
            get => _ciente;
            set => SetProperty(ref _ciente, value);
        }
    }
}
