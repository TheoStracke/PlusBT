using System.ComponentModel;

namespace Busca_BT.Models
{
    /// <summary>
    /// Representa uma etiqueta importada da planilha Excel.
    /// Mapeada com a tabela [Labels] no SQL Server (LabelFilePath vem do JOIN com [Templates]).
    /// </summary>
    public class LabelRecord : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public int Item { get; set; }
        public int BatchId { get; set; }
        public string Invoice { get; set; } = string.Empty;
        public string Codigo { get; set; } = string.Empty;
        public string DescricaoAnvisa { get; set; } = string.Empty;
        public int QtdInvoice { get; set; }
        public string Lote { get; set; } = string.Empty;
        public DateTime Validade { get; set; }
        public string RegistroAnvisa { get; set; } = string.Empty;
        public string Lpn { get; set; } = string.Empty;

        /// <summary>
        /// Unidade de destino ("Extrema", "Palhoça" ou vazio), lida da planilha na importação.
        /// Não é gravada no banco — só alimenta o relatório gerado na importação.
        /// </summary>
        public string Local { get; set; } = string.Empty;

        /// <summary>Caminho absoluto do arquivo .btw ou .pdf associado.</summary>
        public string? LabelFilePath { get; set; }

        /// <summary>Em UTC (como gravado no banco).</summary>
        public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Em UTC (como gravado no banco).</summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>Data/hora (UTC) em que o arquivo foi aberto pela primeira vez (via botão Abrir). Null = nunca aberta.</summary>
        public DateTime? AbertaEm
        {
            get => _abertaEm;
            set
            {
                if (_abertaEm == value) return;
                _abertaEm = value;
                // Notifica a grid para o botão virar "Aberto" e a linha ser destacada na hora.
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AbertaEm)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FoiAberta)));
            }
        }
        private DateTime? _abertaEm;

        public bool FoiAberta => AbertaEm.HasValue;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool HasFile => !string.IsNullOrWhiteSpace(LabelFilePath);
    }
}
