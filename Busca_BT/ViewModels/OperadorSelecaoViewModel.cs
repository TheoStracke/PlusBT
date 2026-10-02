using Busca_BT.Data;
using Busca_BT.Infrastructure;
using Busca_BT.Models;
using Busca_BT.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace Busca_BT.ViewModels;

/// <summary>
/// Tela "Quem está operando?": cartões com os operadores ativos; quem tem PIN
/// digita o PIN antes de entrar.
/// </summary>
public sealed partial class OperadorSelecaoViewModel(
    IOperadorRepository repo,
    ISessaoOperador sessao,
    IEventoService eventos,
    ILogger<OperadorSelecaoViewModel> logger) : ObservableObject
{
    public ObservableCollection<Operador> Operadores { get; } = [];

    /// <summary>Operador escolhido que ainda precisa digitar o PIN.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PedindoPin))]
    private Operador? _selecionado;

    public bool PedindoPin => Selecionado is not null;

    [ObservableProperty]
    private string _pin = string.Empty;

    [ObservableProperty]
    private string? _erroPin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemErroCarregar))]
    private string? _erroCarregar;

    public bool TemErroCarregar => ErroCarregar is not null;

    [ObservableProperty]
    private bool _carregando;

    /// <summary>Pedido para abrir a tela de Configurações (sem banco não há operadores).</summary>
    public event Action? AbrirConfiguracoesSolicitado;

    [RelayCommand]
    public async Task CarregarAsync()
    {
        Carregando = true;
        ErroCarregar = null;
        Selecionado = null;

        try
        {
            var lista = await repo.ListarAsync(somenteAtivos: true);
            Operadores.Clear();
            foreach (var op in lista)
                Operadores.Add(op);

            if (Operadores.Count == 0)
                ErroCarregar = "Nenhum operador ativo cadastrado.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao carregar operadores");
            ErroCarregar = $"Não foi possível carregar os operadores: {ex.Message}";
        }
        finally
        {
            Carregando = false;
        }
    }

    [RelayCommand]
    private async Task EscolherAsync(Operador? operador)
    {
        if (operador is null)
            return;

        if (!operador.TemPin)
        {
            await EntrarAsync(operador);
            return;
        }

        Pin = string.Empty;
        ErroPin = null;
        Selecionado = operador;
    }

    [RelayCommand]
    private async Task ConfirmarPinAsync()
    {
        if (Selecionado is not { } operador)
            return;

        if (!PinHasher.Verify(Pin, operador.PinHash))
        {
            ErroPin = "PIN incorreto.";
            Pin = string.Empty;
            return;
        }

        await EntrarAsync(operador);
    }

    [RelayCommand]
    private void Voltar()
    {
        Selecionado = null;
        Pin = string.Empty;
        ErroPin = null;
    }

    [RelayCommand]
    private void AbrirConfiguracoes() => AbrirConfiguracoesSolicitado?.Invoke();

    private async Task EntrarAsync(Operador operador)
    {
        Selecionado = null;
        Pin = string.Empty;
        ErroPin = null;

        sessao.Entrar(operador);
        await eventos.RegistrarAsync(Acoes.Entrou);
    }
}
