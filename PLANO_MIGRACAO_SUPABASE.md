# PlusBT: plano de migração para o Supabase

> Plano definido em 02/10/2026, a partir das respostas da operação em [`ANALISE_UX_OPERADOR.md`](ANALISE_UX_OPERADOR.md).

## Contexto (respostas da operação)

- **Dois PCs, na mesma rede:**
  - **Impressão:** só imprime.
  - **Administração:** envia a planilha e cadastra/atualiza os templates.
- **Internet instável no PC de impressão.** A fila precisa continuar carregada mesmo sem conexão.
- **Operadores** identificados por **nome + PIN opcional**.
- **Templates (`.btw`)** continuam na pasta de rede `\\Serveradeprint\arquivos\Etiquetas_PlusBT`.
- Lote, validade e quantidade hoje são **digitados à mão** no BarTender.
- As etiquetas são **conferidas** contra o relatório.
- Chega cerca de **uma planilha por semana**.
- **Não há** leitor de código de barras.

---

## Arquitetura

```
              ┌──────────── Supabase (Postgres) ─────────────┐
              │ operadores · invoices/itens · templates ·    │
              │ importações · eventos (quem fez o quê)       │
              └───────▲──────────────────────────▲───────────┘
                      │ internet                 │ internet (instável)
          ┌───────────┴─────────┐     ┌──────────┴──────────────────────┐
          │ PC ADMINISTRAÇÃO    │     │ PC IMPRESSÃO                    │
          │ importa planilha,   │     │ cópia local da fila (SQLite)    │
          │ cadastra templates  │     │ funciona sem internet e envia   │
          └───────────┬─────────┘     │ tudo quando a conexão volta     │
                      │               └──────────┬──────────────────────┘
                      └──── rede local ──────────┘
                     \\Serveradeprint\...\Etiquetas_PlusBT  (.btw)
```

## Decisões

### 1. Conexão com o banco
- O app conecta **direto no Postgres do Supabase**, usando **Npgsql + Dapper**. Isso aproveita a maior parte da camada de dados atual.
- Usa um **usuário do banco dedicado ao app** (`plusbt_app`), com permissão só nas tabelas do sistema. Não é o usuário `postgres`.
- A connection string fica nas Configurações do PC, **criptografada com DPAPI**. **Nunca** vai para o repositório nem fica embutida no `.exe`.
- **Por que não a API REST com login do Supabase:** como o operador entra com nome e PIN (e não com login real por usuário), a API com login não traria ganho de segurança que justifique reescrever toda a camada de dados.

### 2. Modo offline (PC de impressão)
- A fila, os templates e os operadores ficam numa **cópia local em SQLite**, e a tela **sempre lê dessa cópia**.
- Abrir etiqueta, dar ciência de pendência e conferir **funcionam sem internet**. Essas ações vão para uma **fila de envio** local e são sincronizadas quando a conexão volta.
- A sincronização roda em segundo plano, a cada 30–60 s e ao recuperar a conexão.
- Indicador no topo: 🟢 *Online* / 🟠 *Offline, última sincronização 10:42* / 🔄 *Sincronizando…*
- **Importar planilha** e **cadastrar template** exigem internet. As duas coisas são feitas no PC de administração.

### 3. Templates (revisado em 02/10/2026)
- Os **arquivos `.btw` ficam no Supabase**, numa tabela, com **hash SHA-256** de cada versão. O acervo atual tem 880 arquivos e 39 MB (maior arquivo: 60 KB).
- **Versionamento:** cada envio cria uma versão nova. Ficam guardadas a **atual e as 3 anteriores** como backup. Enviar um arquivo idêntico (mesmo hash) não cria versão.
- **Cópia local em cada PC** (`C:\ProgramData\BuscaBT\Templates`): a sincronização baixa só o que mudou e confere o hash. **Abrir etiqueta usa sempre a cópia local**, então funciona sem internet.
- **Só administrador envia:** adicionar arquivos, importar uma pasta inteira, ou "Enviar nova versão" de um template.
- **Edição no BarTender:** o admin edita a cópia local; o app mostra "Alterado neste PC" e o botão **"Enviar alteração"** faz o upload como nova versão. Se outra pessoa enviou uma versão nesse meio-tempo, o app avisa antes de substituir.
- **PC de impressão:** se o arquivo local for alterado por alguém com perfil Impressão, a alteração é guardada em `_descartados` e o arquivo volta à versão do banco.
- **Restaurar versão:** cria uma versão nova com o conteúdo antigo; o histórico nunca se perde.
- **Modo por PC (Configurações):** **Banco de dados** (padrão, tudo acima) ou **Pasta local** (abre direto de uma pasta escolhida, por exemplo `\\Serveradeprint\...`, pelo vínculo tolerante; serve de contingência). Há também **"Exportar acervo"** para gravar todos os templates numa pasta.
- A **carga inicial** dos 880 arquivos é feita por um administrador, no app, com "Importar pasta" apontando para `\\Serveradeprint\arquivos\Etiquetas_PlusBT`.

### 4. Operadores
- Ao abrir o app, aparece a tela **"Quem está operando?"**, com os operadores em cartões e um **PIN opcional**.
- O PIN fica guardado como **hash** (irreversível). A lista também fica na cópia local, então o login funciona **offline**.
- O nome do operador aparece no topo, com a opção **"Trocar operador"**.
- **Perfis:**
  - **Administrador:** importa planilhas, cadastra templates e operadores, limpa a fila.
  - **Impressão:** abre etiquetas, dá ciência de pendência, confere.
- Toda ação relevante gera um registro na tabela **`eventos`**, com operador, PC, data e hora.

---

## Estrutura do banco (Postgres, rascunho)

| Tabela | Colunas principais |
|---|---|
| `operadores` | `id`, `nome`, `pin_hash` (nulo = sem PIN), `perfil` (`admin` / `impressao`), `ativo`, `criado_em` |
| `importacoes` | `id`, `arquivo`, `importado_em`, `importado_por` → operadores, `total`, `importados`, `ignorados` |
| `itens` | `id`, `importacao_id`, `item`, `invoice`, `codigo`, `descricao`, `qtd`, `lote`, `validade` (nulo se inválida), `validade_texto`, `registro_anvisa`, `lpn`, `local`, `avisos`, `aberta_em`, `aberta_por`, `conferida_em`, `conferida_por`, `atualizado_em` |
| `templates` | `id`, `codigo` (único), `nome_arquivo`, `versao_atual`, `hash_atual`, `atualizado_em`, `atualizado_por` |
| `template_versoes` | `template_id`, `versao`, `nome_arquivo`, `conteudo` (o `.btw`), `hash_sha256`, `tamanho`, `origem` (upload / importacao_pasta / edicao / restauracao), `enviado_em`, `enviado_por` |
| `eventos` | `id`, `ocorrido_em`, `operador_id`, `pc`, `acao` (importou, abriu, ciencia, conferiu, limpou_fila, template_alterado…), `invoice`, `codigo`, `detalhes` (jsonb) |
| `schema_versao` | Versão dos scripts de migração já aplicados |

- Os scripts ficam versionados no repositório (`Busca_BT/Data/Migrations/001_inicial.sql`, …) e são aplicados pelo app ao iniciar.
- **Sincronização offline:** cada ação feita no PC de impressão leva um `id` gerado localmente (UUID). Assim, reenviar a mesma ação depois de uma queda não a duplica.

---

## Etapas

| # | Etapa | Entrega |
|---|---|---|
| 1 | **Banco na nuvem** | Scripts da estrutura no Postgres; camada de dados trocada para Npgsql; importação em lote (rápida mesmo com internet lenta); tela de Configurações para o Supabase; fim da busca automática de servidor SQL na rede |
| 2 | **Operadores** | Tela de seleção com PIN; perfis; cadastro de operadores (Administrador); tabela de eventos |
| 3 | **Offline** | Cópia local (SQLite) no PC de impressão; fila de envio; sincronização automática; indicador online/offline |
| 4 | **Acervo de templates no banco** | 4.1 tabela de versões com hash · 4.2 upload (arquivos e pasta) com versionamento e 3 backups · 4.3 cópia local baixando só o que mudou · 4.4 abrir sempre pela cópia local · 4.5 "Enviar alteração" com aviso de conflito e proteção no PC de impressão · 4.6 tela Templates com status, versões e restaurar · 4.7 modo Banco / Pasta local e "Exportar acervo" |
| 5 | **Carga inicial** | Um administrador importa os 880 `.btw` de `\\Serveradeprint` pelo app ("Importar pasta"); o histórico do SQL Server não é migrado (era banco de teste) |
| — | **Virada** | Testar nos dois PCs e distribuir o `.exe` |

### Depois da migração
- **Dados preenchidos no BarTender:** o sistema envia lote, validade e quantidade e acaba com a digitação manual. Exige que os `.btw` tenham campos nomeados, um ajuste feito uma vez em cada template.
- **Modo conferência:** marcar cada item como conferido na tela, com o operador registrado. Substitui a conferência no papel.
- As correções rápidas da análise (Fase 1 do [`ANALISE_UX_OPERADOR.md`](ANALISE_UX_OPERADOR.md)), por exemplo o botão "Fechar" da tela Templates que encerra o app.

---

## Riscos e cuidados

| Risco | Como tratar |
|---|---|
| Credencial do banco extraída de um PC | Usuário `plusbt_app` com permissão mínima; senha criptografada (DPAPI); trocar a senha se um PC for perdido |
| Internet cai durante uma importação | A importação roda numa transação única: entra tudo ou nada; o resumo avisa |
| Ação feita offline duplicada ao sincronizar | `id` gerado localmente e envio que pode ser repetido sem duplicar |
| Plano gratuito do Supabase pausa projeto parado | Conferir as regras atuais do plano antes da produção; a sincronização periódica mantém o projeto ativo |
| Pasta de rede indisponível | A cópia local mantém a fila visível; o aviso "Arquivo do template não encontrado" indica o problema |

---

## Pendências para começar

- [ ] Criar o projeto no Supabase e informar a **connection string do Postgres** (*Project Settings → Database → Session pooler*). Ela será gravada só nas Configurações do app.
- [ ] Decidir se o **histórico de importações** do SQL Server também será migrado, ou só o acervo de templates.
- [ ] Informar os **nomes dos operadores** e quem é **Administrador**, ou começar com um administrador que cadastra os demais pelo app.
