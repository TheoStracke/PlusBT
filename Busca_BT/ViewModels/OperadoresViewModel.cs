using Busca_BT.Data;
using Busca_BT.Infrastructure;
using Busca_BT.Models;
using Busca_BT.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Collections.ObjectModel;

namespace Busca_BT.ViewModels;

/// <summary>Cadastro de operadores (só administrador).</summary>
public sealed partial class OperadoresViewModel(
    IOperadorRepository repo,
    ISessaoOperador sessao,
    IEventoService eventos,
    ILogger<OperadoresViewModel> logger) : ObservableObject
{
    public ObservableCollection<Operador> Operadores { get; } = [];

    public IReadOnlyList<PerfilOpcao> Perfis { get; } =
    [
        new(Operador.PerfilImpressao, "Impressão — abre, imprime e confere etiquetas"),
        new(Operador.PerfilAdmin, "Administrador — também importa planilhas e cuida de templates e operadores"),
    ];

    /// <summary>Operador em edição (null = cadastrando um novo).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo), nameof(EditandoExistente), nameof(TemPinAtual))]
    private Operador? _selecionado;

    public bool EditandoExistente => Selecionado is not null;
    public bool TemPinAtual => Selecionado?.TemPin == true;
    public string Titulo => Selecionado is null ? "Novo operador" : $"Editar {Selecionado.Nome}";

    [ObservableProperty]
    private string _nome = string.Empty;

    [ObservableProperty]
    private string _perfil = Operador.PerfilImpressao;

    [ObservableProperty]
    private bool _ativo = true;

    /// <summary>Novo PIN (vazio = mantém o atual / sem PIN se for novo).</summary>
    [ObservableProperty]
    private string _novoPin = string.Empty;

    [ObservableProperty]
    private bool _removerPin;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _statusErro;

    partial void OnSelecionadoChanged(Operador? value)
    {
        Nome = value?.Nome ?? string.Empty;
        Perfil = value?.Perfil ?? Operador.PerfilImpressao;
        Ativo = value?.Ativo ?? true;
        NovoPin = string.Empty;
        RemoverPin = false;
        StatusText = string.Empty;
    }

    [RelayCommand]
    public async Task CarregarAsync()
    {
        try
        {
            var selecionadoId = Selecionado?.Id;
            var lista = await repo.ListarAsync(somenteAtivos: false);

            Operadores.Clear();
            foreach (var op in lista)
                Operadores.Add(op);

            Selecionado = Operadores.FirstOrDefault(o => o.Id == selecionadoId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao carregar operadores");
            Mostrar($"Não foi possível carregar os operadores: {ex.Message}", erro: true);
        }
    }

    [RelayCommand]
    private void Novo() => Selecionado = null;

    [RelayCommand]
    private async Task SalvarAsync()
    {
        var nome = Nome.Trim();
        if (nome.Length == 0)
        {
            Mostrar("Informe o nome.", erro: true);
            return;
        }

        if (NovoPin.Length > 0 && !PinHasher.IsValid(NovoPin))
        {
            Mostrar("O PIN deve ter de 4 a 8 números.", erro: true);
            return;
        }

        // Regras de segurança: ninguém se tranca para fora, e sempre sobra um administrador.
        if (Selecionado is { } atual)
        {
            var ehVoce = atual.Id == sessao.Atual?.Id;
            if (ehVoce && (!Ativo || Perfil != Operador.PerfilAdmin))
            {
                Mostrar("Você não pode desativar nem tirar o perfil de administrador de si mesmo.", erro: true);
                return;
            }

            var outrosAdminsAtivos = Operadores.Count(o => o.Id != atual.Id && o.IsAdmin && o.Ativo);
            if (atual.IsAdmin && (!Ativo || Perfil != Operador.PerfilAdmin) && outrosAdminsAtivos == 0)
            {
                Mostrar("É preciso manter pelo menos um administrador ativo.", erro: true);
                return;
            }
        }

        try
        {
            string acao;
            if (Selecionado is null)
            {
                var criado = await repo.CriarAsync(nome, Perfil, NovoPin.Length > 0 ? PinHasher.Hash(NovoPin) : null);
                acao = "criado";
                await eventos.RegistrarAsync(Acoes.OperadorAlterado, detalhes: new { operador = nome, acao, perfil = Perfil, pin = NovoPin.Length > 0 });
                await CarregarAsync();
                Selecionado = Operadores.FirstOrDefault(o => o.Id == criado.Id);
            }
            else
            {
                var id = Selecionado.Id;
                await repo.AtualizarAsync(id, nome, Perfil, Ativo);

                string? pin = null;
                if (RemoverPin)
                {
                    await repo.DefinirPinAsync(id, null);
                    pin = "removido";
                }
                else if (NovoPin.Length > 0)
                {
                    await repo.DefinirPinAsync(id, PinHasher.Hash(NovoPin));
                    pin = "alterado";
                }

                acao = "alterado";
                await eventos.RegistrarAsync(Acoes.OperadorAlterado, detalhes: new { operador = nome, acao, perfil = Perfil, ativo = Ativo, pin });
                await CarregarAsync();
            }

            Mostrar($"Operador {nome} {acao}.", erro: false);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            Mostrar($"Já existe um operador chamado {nome}.", erro: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao salvar operador");
            Mostrar($"Não foi possível salvar: {ex.Message}", erro: true);
        }
    }

    private void Mostrar(string texto, bool erro)
    {
        StatusText = texto;
        StatusErro = erro;
    }
}

public sealed record PerfilOpcao(string Valor, string Texto);
