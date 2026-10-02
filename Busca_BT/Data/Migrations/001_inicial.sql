-- PlusBT: estrutura inicial no Supabase (Postgres).
--
-- Tudo fica no schema "plusbt", que NÃO é exposto pela API REST do Supabase (só o
-- "public" é). Além disso, RLS fica ligado sem nenhuma policy: mesmo que alguém use a
-- chave anon da API, não lê nada. O app conecta direto no Postgres como dono das
-- tabelas, e o dono não é afetado pelo RLS.
--
-- Aplicado automaticamente pelo app ao iniciar (DatabaseInitializer), uma única vez.

create schema if not exists plusbt;

-- ── Operadores ──────────────────────────────────────────────────────────────
create table plusbt.operadores (
    id          uuid primary key default gen_random_uuid(),
    nome        text not null unique,
    pin_hash    text,                                   -- null = entra sem PIN
    perfil      text not null check (perfil in ('admin', 'impressao')),
    ativo       boolean not null default true,
    criado_em   timestamptz not null default now()
);

-- ── Importações de planilha ─────────────────────────────────────────────────
create table plusbt.importacoes (
    id              integer generated always as identity primary key,
    arquivo         text not null,
    importado_em    timestamptz not null default now(),
    importado_por   uuid references plusbt.operadores (id),
    total           integer not null,
    importados      integer not null,
    ignorados       integer not null
);

-- ── Itens da fila (uma linha da planilha = um item) ─────────────────────────
create table plusbt.itens (
    id               integer generated always as identity primary key,
    importacao_id    integer not null references plusbt.importacoes (id) on delete cascade,
    item             integer not null,
    invoice          text not null,
    codigo           text not null default '',
    descricao        text not null default '',
    qtd              integer not null default 0,
    lote             text not null default '',
    validade         date,                              -- null quando a planilha trouxe data inválida
    validade_texto   text,                              -- texto original da validade inválida
    registro_anvisa  text not null default '',
    lpn              text not null default '',
    local            text not null default '',
    avisos           text,                              -- problemas da importação, um por linha
    aberta_em        timestamptz,
    aberta_por       uuid references plusbt.operadores (id),
    conferida_em     timestamptz,
    conferida_por    uuid references plusbt.operadores (id),
    importado_em     timestamptz not null default now(),
    atualizado_em    timestamptz
);

create index itens_importacao_idx on plusbt.itens (importacao_id);
create index itens_codigo_idx on plusbt.itens (codigo);

-- ── Acervo de templates (.btw) ──────────────────────────────────────────────
create table plusbt.templates (
    id              integer generated always as identity primary key,
    codigo          text not null unique,
    arquivo         text,
    atualizado_em   timestamptz not null default now(),
    atualizado_por  uuid references plusbt.operadores (id)
);

-- ── Eventos (quem fez o quê) ────────────────────────────────────────────────
-- O id é gerado no PC (uuid) para que o reenvio de uma ação feita offline não duplique.
create table plusbt.eventos (
    id           uuid primary key default gen_random_uuid(),
    ocorrido_em  timestamptz not null default now(),
    operador_id  uuid references plusbt.operadores (id),
    pc           text not null default '',
    acao         text not null,
    invoice      text,
    codigo       text,
    detalhes     jsonb
);

create index eventos_ocorrido_idx on plusbt.eventos (ocorrido_em desc);

-- ── Segurança: bloqueia a API REST do Supabase ──────────────────────────────
alter table plusbt.operadores  enable row level security;
alter table plusbt.importacoes enable row level security;
alter table plusbt.itens       enable row level security;
alter table plusbt.templates   enable row level security;
alter table plusbt.eventos     enable row level security;

revoke all on schema plusbt from anon, authenticated;
revoke all on all tables in schema plusbt from anon, authenticated;

-- ── Operadores iniciais ─────────────────────────────────────────────────────
insert into plusbt.operadores (nome, perfil) values
    ('Theo',    'admin'),
    ('Cris',    'admin'),
    ('Gustavo', 'impressao'),
    ('Rafael',  'impressao')
on conflict (nome) do nothing;
