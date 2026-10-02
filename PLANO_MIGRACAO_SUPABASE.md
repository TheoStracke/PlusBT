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

### 3. Templates
- Continuam na **pasta de rede local**, que não depende de internet.
- O banco guarda **só o nome do arquivo** (`C76421.btw`), e não o caminho completo.
- Cada PC configura a sua **pasta de templates** nas Configurações.
- O vínculo tolerante continua valendo (`LabelDiagnostics`): ignora maiúsculas, espaços, zeros à esquerda e sufixos.
- Isso corrige o problema de hoje, em que "Sincronizar pasta" e "Substituir arquivo" gravam um caminho `C:\ProgramData` no banco.

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
| `templates` | `id`, `codigo` (único), `arquivo` (só o nome), `atualizado_em`, `atualizado_por` |
| `eventos` | `id`, `ocorrido_em`, `operador_id`, `pc`, `acao` (importou, abriu, ciencia, conferiu, limpou_fila, template_alterado…), `invoice`, `codigo`, `detalhes` (jsonb) |
| `schema_versao` | Versão dos scripts de migração já aplicados |

- Os scripts ficam versionados no repositório (`db/migrations/001_inicial.sql`, …).
- **Sincronização offline:** cada ação feita no PC de impressão leva um `id` gerado localmente (UUID). Assim, reenviar a mesma ação depois de uma queda não a duplica.

---

## Etapas

| # | Etapa | Entrega |
|---|---|---|
| 1 | **Banco na nuvem** | Scripts da estrutura no Postgres; camada de dados trocada para Npgsql; importação em lote (rápida mesmo com internet lenta); tela de Configurações para o Supabase; fim da busca automática de servidor SQL na rede |
| 2 | **Operadores** | Tela de seleção com PIN; perfis; cadastro de operadores (Administrador); tabela de eventos |
| 3 | **Offline** | Cópia local (SQLite) no PC de impressão; fila de envio; sincronização automática; indicador online/offline |
| 4 | **Templates por nome** | Pasta de templates configurável por PC; conversão dos caminhos atuais para nome de arquivo; ajuste da tela Templates |
| 5 | **Migração dos dados** | Copiar o acervo de templates (e, se decidido, o histórico) do SQL Server para o Supabase |
| — | **Virada** | Testar nos dois PCs; o SQL Server atual continua funcionando até a virada, e serve de volta segura se algo der errado |

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
