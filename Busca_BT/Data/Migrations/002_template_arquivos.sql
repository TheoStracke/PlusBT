-- PlusBT: acervo de templates (.btw) guardado no banco, com versões e hash.
--
-- templates          → um registro por código, apontando para a versão atual.
-- template_versoes   → o arquivo de cada versão (bytea), com hash SHA-256.
-- O app guarda a versão atual + as 3 anteriores (as mais antigas são apagadas no envio).

alter table plusbt.templates add column if not exists nome_arquivo text;
alter table plusbt.templates add column if not exists versao_atual integer not null default 0;  -- 0 = sem arquivo no banco
alter table plusbt.templates add column if not exists hash_atual text;

create table plusbt.template_versoes (
    id            integer generated always as identity primary key,
    template_id   integer not null references plusbt.templates (id) on delete cascade,
    versao        integer not null,
    nome_arquivo  text not null,
    conteudo      bytea not null,
    hash_sha256   text not null,
    tamanho       integer not null,
    origem        text not null check (origem in ('upload', 'importacao_pasta', 'edicao', 'restauracao')),
    enviado_em    timestamptz not null default now(),
    enviado_por   uuid references plusbt.operadores (id),
    unique (template_id, versao)
);

alter table plusbt.template_versoes enable row level security;
revoke all on plusbt.template_versoes from anon, authenticated;
