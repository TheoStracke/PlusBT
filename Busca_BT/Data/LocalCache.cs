using Busca_BT.Models;
using Microsoft.Data.Sqlite;
using System.IO;

namespace Busca_BT.Data;

/// <summary>Ação feita neste PC aguardando envio ao Supabase.</summary>
public sealed record AcaoPendente(long Seq, string Tipo, string Payload, int Tentativas);

/// <summary>
/// Cópia local (SQLite) da fila, dos templates e dos operadores, mais a fila de envio
/// das ações feitas neste PC. As telas leem SEMPRE daqui, então funcionam sem
/// internet; o SyncService mantém a cópia atualizada e envia as ações pendentes.
/// Arquivo: %LocalAppData%\BuscaBT\cache.db (um por usuário do Windows).
/// </summary>
public sealed class LocalCache
{
    private readonly string _connectionString;

    public LocalCache() : this(null) { }

    /// <param name="caminho">Arquivo do banco local (null = o padrão em %LocalAppData%; outro só em testes).</param>
    public LocalCache(string? caminho)
    {
        if (caminho is null)
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BuscaBT");
            Directory.CreateDirectory(folder);
            caminho = Path.Combine(folder, "cache.db");
        }

        _connectionString = new SqliteConnectionStringBuilder { DataSource = caminho }.ToString();

        CriarTabelas();
    }

    private SqliteConnection Abrir()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private void CriarTabelas()
    {
        using var conn = Abrir();
        Executar(conn, """
            pragma journal_mode = wal;

            create table if not exists itens (
                id integer primary key, item integer, importacao_id integer, invoice text, codigo text,
                descricao text, qtd integer, lote text, validade integer, validade_texto text,
                registro_anvisa text, lpn text, local text, avisos text, importado_em integer,
                atualizado_em integer, aberta_em integer, aberta_por text
            );
            create table if not exists templates (
                id integer primary key, codigo text, arquivo text, atualizado_em integer
            );
            create table if not exists operadores (
                id text primary key, nome text, pin_hash text, perfil text, ativo integer
            );
            create table if not exists pendentes (
                seq integer primary key autoincrement, tipo text not null, payload text not null,
                criado_em integer not null, tentativas integer not null default 0, ultimo_erro text
            );
            create table if not exists meta (chave text primary key, valor text);
            """);
    }

    // ── Leitura ──────────────────────────────────────────────────────────────

    public IReadOnlyList<LabelRecord> LerItens()
    {
        using var conn = Abrir();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            select id, item, importacao_id, invoice, codigo, descricao, qtd, lote, validade, validade_texto,
                   registro_anvisa, lpn, local, avisos, importado_em, atualizado_em, aberta_em, aberta_por
            from itens order by id;
            """;

        var lista = new List<LabelRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            lista.Add(new LabelRecord
            {
                Id = r.GetInt32(0),
                Item = r.GetInt32(1),
                BatchId = r.GetInt32(2),
                Invoice = Texto(r, 3),
                Codigo = Texto(r, 4),
                DescricaoAnvisa = Texto(r, 5),
                QtdInvoice = r.GetInt32(6),
                Lote = Texto(r, 7),
                Validade = Data(r, 8, DateTimeKind.Unspecified),
                ValidadeTexto = TextoOuNull(r, 9),
                RegistroAnvisa = Texto(r, 10),
                Lpn = Texto(r, 11),
                Local = Texto(r, 12),
                Avisos = TextoOuNull(r, 13),
                ImportedAt = Data(r, 14, DateTimeKind.Utc) ?? DateTime.UtcNow,
                UpdatedAt = Data(r, 15, DateTimeKind.Utc),
                AbertaPor = TextoOuNull(r, 17),
                AbertaEm = Data(r, 16, DateTimeKind.Utc)
            });
        }
        return lista;
    }

    public IReadOnlyList<LabelRecord> LerTemplates()
    {
        using var conn = Abrir();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "select id, codigo, arquivo, atualizado_em from templates order by codigo;";

        var lista = new List<LabelRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            lista.Add(new LabelRecord
            {
                Id = r.GetInt32(0),
                Codigo = Texto(r, 1),
                LabelFilePath = TextoOuNull(r, 2),
                UpdatedAt = Data(r, 3, DateTimeKind.Utc)
            });
        }
        return lista;
    }

    public IReadOnlyList<Operador> LerOperadores(bool somenteAtivos)
    {
        using var conn = Abrir();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "select id, nome, pin_hash, perfil, ativo from operadores "
                        + (somenteAtivos ? "where ativo = 1 " : "") + "order by ativo desc, nome;";

        var lista = new List<Operador>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            lista.Add(new Operador
            {
                Id = Guid.Parse(r.GetString(0)),
                Nome = Texto(r, 1),
                PinHash = TextoOuNull(r, 2),
                Perfil = Texto(r, 3),
                Ativo = r.GetInt64(4) != 0
            });
        }
        return lista;
    }

    public bool TemOperadores()
    {
        using var conn = Abrir();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*) from operadores;";
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    // ── Cópia vinda do Supabase ──────────────────────────────────────────────

    /// <summary>Substitui a cópia local inteira pelo que veio do Supabase (numa transação).</summary>
    public void SubstituirTudo(
        IReadOnlyList<LabelRecord> itens, IReadOnlyList<LabelRecord> templates, IReadOnlyList<Operador> operadores)
    {
        using var conn = Abrir();
        using var tx = conn.BeginTransaction();

        Executar(conn, "delete from itens; delete from templates; delete from operadores;", tx);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                insert into itens values ($id, $item, $imp, $invoice, $codigo, $descricao, $qtd, $lote, $validade,
                    $validade_texto, $registro, $lpn, $local, $avisos, $importado_em, $atualizado_em, $aberta_em, $aberta_por);
                """;
            foreach (var i in itens)
            {
                cmd.Parameters.Clear();
                Param(cmd, "$id", i.Id); Param(cmd, "$item", i.Item); Param(cmd, "$imp", i.BatchId);
                Param(cmd, "$invoice", i.Invoice); Param(cmd, "$codigo", i.Codigo); Param(cmd, "$descricao", i.DescricaoAnvisa);
                Param(cmd, "$qtd", i.QtdInvoice); Param(cmd, "$lote", i.Lote); Param(cmd, "$validade", Ticks(i.Validade));
                Param(cmd, "$validade_texto", i.ValidadeTexto); Param(cmd, "$registro", i.RegistroAnvisa);
                Param(cmd, "$lpn", i.Lpn); Param(cmd, "$local", i.Local); Param(cmd, "$avisos", i.Avisos);
                Param(cmd, "$importado_em", Ticks(i.ImportedAt)); Param(cmd, "$atualizado_em", Ticks(i.UpdatedAt));
                Param(cmd, "$aberta_em", Ticks(i.AbertaEm)); Param(cmd, "$aberta_por", i.AbertaPor);
                cmd.ExecuteNonQuery();
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "insert into templates values ($id, $codigo, $arquivo, $atualizado_em);";
            foreach (var t in templates)
            {
                cmd.Parameters.Clear();
                Param(cmd, "$id", t.Id); Param(cmd, "$codigo", t.Codigo);
                Param(cmd, "$arquivo", t.LabelFilePath); Param(cmd, "$atualizado_em", Ticks(t.UpdatedAt));
                cmd.ExecuteNonQuery();
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "insert into operadores values ($id, $nome, $pin, $perfil, $ativo);";
            foreach (var o in operadores)
            {
                cmd.Parameters.Clear();
                Param(cmd, "$id", o.Id.ToString()); Param(cmd, "$nome", o.Nome); Param(cmd, "$pin", o.PinHash);
                Param(cmd, "$perfil", o.Perfil); Param(cmd, "$ativo", o.Ativo ? 1 : 0);
                cmd.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }

    // ── Ações feitas neste PC ────────────────────────────────────────────────

    /// <summary>Marca o item como aberto na cópia local (o envio vai pela fila de pendentes).</summary>
    public void MarcarAberta(int id, DateTime quandoUtc, string? porNome)
    {
        using var conn = Abrir();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "update itens set aberta_em = $em, aberta_por = $por where id = $id and aberta_em is null;";
        Param(cmd, "$em", quandoUtc.Ticks);
        Param(cmd, "$por", porNome);
        Param(cmd, "$id", id);
        cmd.ExecuteNonQuery();
    }

    public void Enfileirar(string tipo, string payload)
    {
        using var conn = Abrir();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "insert into pendentes (tipo, payload, criado_em) values ($tipo, $payload, $em);";
        Param(cmd, "$tipo", tipo);
        Param(cmd, "$payload", payload);
        Param(cmd, "$em", DateTime.UtcNow.Ticks);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Ações pendentes, na ordem em que foram feitas.</summary>
    public IReadOnlyList<AcaoPendente> LerPendentes(int maxTentativas)
    {
        using var conn = Abrir();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "select seq, tipo, payload, tentativas from pendentes where tentativas < $max order by seq;";
        Param(cmd, "$max", maxTentativas);

        var lista = new List<AcaoPendente>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new AcaoPendente(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt32(3)));
        return lista;
    }

    public int ContarPendentes(int maxTentativas)
    {
        using var conn = Abrir();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*) from pendentes where tentativas < $max;";
        Param(cmd, "$max", maxTentativas);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void RemoverPendente(long seq)
    {
        using var conn = Abrir();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "delete from pendentes where seq = $seq;";
        Param(cmd, "$seq", seq);
        cmd.ExecuteNonQuery();
    }

    public void RegistrarFalha(long seq, string erro)
    {
        using var conn = Abrir();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "update pendentes set tentativas = tentativas + 1, ultimo_erro = $erro where seq = $seq;";
        Param(cmd, "$erro", erro);
        Param(cmd, "$seq", seq);
        cmd.ExecuteNonQuery();
    }

    // ── Meta (última sincronização etc.) ─────────────────────────────────────

    public DateTime? UltimaSincronizacao
    {
        get
        {
            using var conn = Abrir();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "select valor from meta where chave = 'ultima_sync';";
            return cmd.ExecuteScalar() is string s && long.TryParse(s, out var ticks)
                ? new DateTime(ticks, DateTimeKind.Utc)
                : null;
        }
        set
        {
            using var conn = Abrir();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "insert into meta values ('ultima_sync', $v) on conflict (chave) do update set valor = $v;";
            Param(cmd, "$v", value?.Ticks.ToString());
            cmd.ExecuteNonQuery();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void Executar(SqliteConnection conn, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void Param(SqliteCommand cmd, string nome, object? valor)
        => cmd.Parameters.AddWithValue(nome, valor ?? DBNull.Value);

    // Datas guardadas como ticks (inteiro): sem ambiguidade de formato ou fuso.
    private static long? Ticks(DateTime? d) => d?.Ticks;

    private static DateTime? Data(SqliteDataReader r, int i, DateTimeKind kind)
        => r.IsDBNull(i) ? null : new DateTime(r.GetInt64(i), kind);

    private static string Texto(SqliteDataReader r, int i) => r.IsDBNull(i) ? string.Empty : r.GetString(i);
    private static string? TextoOuNull(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
