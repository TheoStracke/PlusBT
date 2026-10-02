# PlusBT: análise de uso pelo operador (fluxo e UX/UI)

> Análise feita em 02/10/2026, lendo todas as telas e fluxos do sistema do ponto de vista de quem opera no dia a dia: importar a planilha, achar a etiqueta, abrir no BarTender, imprimir e conferir.
> Cada ponto traz **o que acontece hoje**, **o impacto para o operador**, **a sugestão** e **onde mexer no código**.
>
> Gravidade: 🔴 crítico (perde trabalho ou gera erro de etiqueta) · 🟠 alto (atrasa ou confunde todo dia) · 🟡 médio · ⚪ baixo / acabamento

---

## 1. Resumo: as 10 mudanças que mais ajudam o operador

| # | Mudança | Gravidade | Esforço |
|---|---|---|---|
| 1 | O botão **"Fechar"** da tela Templates fecha o **aplicativo inteiro** | 🔴 | Pequeno |
| 2 | Importar uma planilha **apaga a fila anterior e o progresso**. Dar a opção "Adicionar à fila" | 🔴 | Médio |
| 3 | **Sincronizar pasta** e **Substituir arquivo** gravam um caminho **local** (`C:\ProgramData`) no banco compartilhado, e os outros PCs não acham o arquivo | 🔴 | Pequeno |
| 4 | Mostrar **Validade** e **Qtd** em cada linha da Home. Hoje o operador não vê quantas etiquetas imprimir por item | 🟠 | Pequeno |
| 5 | **Modo leitor de código de barras**: bipar o LPN ou o código, apertar Enter e já abrir a etiqueta | 🟠 | Médio |
| 6 | O **log não é gravado em lugar nenhum** (só vai para o console). A ciência de pendência e os erros se perdem | 🟠 | Pequeno |
| 7 | Botão **"Atualizar"** na Home e atualização automática. A fila é compartilhada entre PCs e não se atualiza sozinha | 🟠 | Pequeno |
| 8 | Separar **"Aberto"** de **"Impresso"** e permitir desfazer a marcação | 🟡 | Médio |
| 9 | A tela Templates deve mostrar **quais códigos da fila estão sem template** e permitir vincular direto dali | 🟡 | Médio |
| 10 | **Arrastar e soltar** a planilha na Home para importar | 🟡 | Pequeno |

---

## 2. Como o operador trabalha hoje (jornada)

```
Abre o .exe → splash → Home (fila de invoices)
   │
   ├─ "Importar planilha" → escolhe o .xlsx → resumo (importados / pendências / ignorados)
   │      └─ relatório .xlsx por invoice salvo em Downloads\Relatórios PlusBT\<data-hora>
   │
   ├─ Expande a invoice → lista de itens (Item, Código, Descrição, Lote, LPN, Ação)
   │      └─ "Abrir" → (se tiver pendência: confirma ciência) → abre o .btw no BarTender
   │                   → item fica "Aberto" (amarelo) e a barra de progresso da invoice avança
   │
   ├─ Templates → cadastra e troca os .btw (por pasta, sincronização ou arquivo único)
   ├─ Histórico → lista as importações (pode excluir uma)
   └─ Configurações → servidor SQL
```

**Atritos principais nessa jornada:**
1. Para imprimir, o operador precisa de **lote, validade e quantidade**. A linha mostra só o lote. Validade e Qtd ficam no relatório em Excel, então ele precisa alternar entre as duas telas.
2. Achar um item é **digitar na busca e clicar em Abrir**. Num armazém com leitor de código de barras, isso poderia ser um bipe só.
3. **Importar a segunda planilha do dia apaga a primeira**, inclusive o que já tinha sido aberto.
4. Duas pessoas em PCs diferentes **compartilham a mesma fila** (`dbo.Labels`) sem saber disso. O "Limpar fila" de uma apaga a fila da outra.

---

## 3. Problemas e sugestões por tela

### 3.1 Home (fila de invoices)

#### 🔴 Importar substitui a fila inteira
- **Hoje:** `ReplaceAllAsync` faz `DELETE FROM dbo.Labels` antes de inserir (`Data/LabelRepository.cs:227`). Importar a planilha B apaga a planilha A e todas as marcações de "Aberto".
- **Impacto:** quem recebe várias planilhas por dia perde o progresso, e quem divide a fila com outro PC apaga o trabalho do colega.
- **Sugestão:** ao importar, se a fila não estiver vazia, perguntar:
  - **Adicionar à fila** (padrão). Invoices que já existem são substituídas e as novas, somadas.
  - **Substituir a fila inteira** (comportamento atual).
  - Avisar se a mesma planilha (mesmo nome e mesmas invoices) já foi importada hoje.
	- 
RESPOSTA: Sempre vai substituir mas mantém a outra no histórico

#### 🟠 A linha do item não mostra Validade nem Qtd
- **Hoje:** as colunas são Item, Código, Descrição, Lote, LPN e Ação (`HomeView.xaml`).
- **Impacto:** a quantidade de etiquetas e a validade são justamente o que o operador digita ou confere no BarTender.
- **Sugestão:** adicionar as colunas **Validade** (em vermelho quando inválida ou vencida) e **Qtd**. Para caber, a descrição pode ser encurtada (ela já tem tooltip).

RESPOSTA: Precisar colocar

#### 🟠 Não há modo leitor de código de barras
- **Sugestão:** um campo de busca com foco automático, onde o Enter faz:
  - um único resultado → abre direto (passando pela ciência, se houver pendência);
  - vários resultados → filtra e destaca o primeiro;
  - nenhum resultado → aviso claro: *"LPN CD3210999 não está na fila"*.
- Atalhos: **Ctrl+F** (foco na busca), **Esc** (limpa a busca), **Enter** (abre o item selecionado), **↑/↓** (navega entre os itens).

RESPOSTA: Não precisa

#### 🟠 A fila não se atualiza sozinha
- **Hoje:** só carrega quando a tela é aberta (`HomeView.xaml.cs`). Não existe botão de atualizar.
- **Sugestão:** botão **Atualizar** (F5) e atualização automática a cada 30–60 s, ou ao voltar o foco para a janela. Mostrar "Atualizado às 10:42".

RESPOSTA: Não precisa, *projeto Plus BT 2.0

#### 🟡 "Aberto" não quer dizer "impresso"
- **Hoje:** o item fica "Aberto" ao clicar no botão, mesmo que o operador feche o BarTender sem imprimir. Não dá para desfazer.
- **Sugestão:** três estados: **Pendente → Aberto → Impresso**. O operador marca "Impresso" com um clique, ou isso é feito automaticamente se a impressão passar a ser pelo próprio sistema (ver 3.1, "Impressão direta"). Clique com o botão direito em **Desmarcar**.
RESPOSTA: Quero que sempre que clicar para abrir a outra etiquea abre um modal perguntando: "Imprimiu quantas etiquetas?" que vai ser uma pegadinha sendo sempre a qtd invoice + 1 que no caso é a etiqtyea que vai no espellho
#### 🟡 Filtros e ordenação
- Filtros rápidos (chips): **Todos · Pendentes · Abertos · Com pendência · Sem template**. O "Com pendência" já existe.
- Ordenar invoices por: ordem da planilha, nome ou progresso (concluídas por último).
- Botões **Expandir todas / Recolher todas**.
RESPOSTA: Pode colocar

#### 🟡 Ações por invoice
- No cabeçalho da invoice: **Reabrir relatório**, **Copiar nº da invoice**, **Abrir próxima pendente** (abre o próximo item não aberto, para trabalhar em sequência sem caçar o botão).
RESPOSTA: É uma boa também
#### 🟡 Impressão direta (evolução)
- Se o `.btw` usa campos nomeados (Lote, Validade, Qtd…), o sistema pode **mandar os dados e imprimir** pela linha de comando do BarTender (`/AF=arquivo.btw /P /D=dados.txt`) ou pelo Integration. Isso elimina a digitação manual, que é a maior fonte de erro de etiqueta.
- Validar com o operador **se hoje ele digita lote e validade no BarTender** (ver seção 6).
- RESPOSTA: é possível mas mais para frente
#### ⚪ Acabamento
- O destaque de "Aberto" usa a cor fixa `#FFF4CE`, que fica estranha se um dia o tema escuro for ativado. Trocar por um recurso do tema.
- Fila vazia: além de "Importe uma planilha", mostrar a última importação ("Última: ETIQUETA_2609… às 09:41").
RESPOSTA: Sim
---

### 3.2 Modal de importação / resumo

- 🟡 **Importar por arrastar e soltar** o `.xlsx` na Home, além do botão.
- 🟡 **Lembrar a última pasta** usada no diálogo de abrir arquivo (normalmente Downloads).
- 🟡 Na lista "com pendência", **agrupar por tipo de problema** ("15 com validade inválida", "6 sem template"), em vez de repetir a mesma mensagem 15 vezes, com um botão **"Ver na fila"** que já aplica o filtro.
- ⚪ Ação **"Abrir planilha original"**, para o operador corrigir na origem e reimportar.
RESPOSTA: Sim menos a de abrir a planilha
---

### 3.3 Templates

#### 🔴 "Fechar" encerra o aplicativo
- **Hoje:** `Close()` fecha a janela ativa (`ViewModels/TemplateUpdateViewModel.cs:418`), que é a janela principal.
- **Sugestão:** usar a navegação (`NavigateTo<HomeViewModel>`) ou simplesmente remover o botão, já que existe a barra lateral.
RESPOSTA: Remove o botão

#### 🔴 Caminho local gravado no banco compartilhado
- **Hoje:** "Sincronizar pasta" copia os arquivos para `C:\ProgramData\BuscaBT\Templates\Sync_<data>` (`TemplateUpdateViewModel.cs:51,186`), e "Substituir arquivo" copia para `C:\ProgramData\BuscaBT\Templates` (`:383`). Esse caminho **só existe no PC que fez a operação**, mas fica gravado no banco que todos usam.
- **Impacto:** nos outros PCs o template aparece como "Arquivo do template não encontrado". O acervo atual está em `\\Serveradeprint\arquivos\Etiquetas_PlusBT\`.
- **Sugestão:** criar uma **pasta central configurável** (em Configurações, guardada no banco), por exemplo `\\Serveradeprint\arquivos\Etiquetas_PlusBT`, e copiar sempre para ela. Bloquear o cadastro de caminhos locais (`C:\…`) ou avisar quando acontecer.
RESPOSTA: Não precisa, *projeto Plus BT 2.0

#### 🟠 Coluna "Descrição" sempre vazia
- **Hoje:** a consulta devolve `'' AS DescricaoAnvisa` (`Data/LabelRepository.cs:64`).
- **Sugestão:** preencher com a última descrição vista na fila para aquele código, ou esconder a coluna.
RESPOSTA: Precisa preencher como está na planilha

#### 🟡 Não mostra quais códigos da fila estão sem template
- **Sugestão:** um painel no topo, *"6 códigos da fila sem template: A98856, 33560…"*. Clicar num código abre o seletor de arquivo já sugerindo o nome `A98856.btw`.
- O mesmo atalho na Home: o botão "Ver motivo" de um item sem template pode oferecer **"Vincular arquivo agora"**.
RESPOSTA: Pode colocar

#### 🟡 Status e lentidão com a pasta de rede
- **Hoje:** `TemArquivo` chama `File.Exists` a cada renderização (`TemplateUpdateViewModel.cs:34`), e o status e a cor chamam de novo. Na rede isso trava a lista.
- **Sugestão:** checar uma vez ao carregar, em segundo plano, e guardar o resultado.
RESPOSTA: Pode colocar

#### 🟡 Mensagens confusas
- "Importar arquivo" mostra *"Nenhum registro encontrado com código…"* (`:298`), mas o upsert sempre insere. A mensagem nunca deveria aparecer.
- Os três botões de importação ("Sincronizar pasta", "Importar pasta", "Importar arquivo") são difíceis de distinguir. Sugestão: um botão só, **"Adicionar templates"**, com um menu:
  - *Um arquivo…*
  - *Uma pasta (manter onde está)…*
  - *Uma pasta (copiar para a pasta central)…*
- Falta **remover** um template ou **desvincular** um arquivo.

---
Quero só dois botões, um para selecionar a pasta de template e outro para importar a planilham, os outros eram outras coisas que eu ia fazer
### 3.4 Histórico

- 🟠 **Mostrar o que foi importado:** clicar numa importação abre as invoices e os itens dela, com as pendências.
- 🟡 **Reabrir o relatório** daquela importação, ou gerá-lo de novo.
- 🟡 **"Excluir"** apaga os itens da fila (`DeleteBatchAsync`). Deixar isso claro (*"remove N itens da fila atual"*) ou trocar por "Arquivar".
- 🟡 Colunas mais úteis: **quem importou** (usuário do Windows ou PC), **invoices**, **itens com pendência**.
- ⚪ Filtro por data e busca por nome de arquivo ou invoice.
- ⚪ "Fechar" leva para uma tela em branco (`NavigateToNull`). Melhor voltar para a Home ou remover o botão.

RESPOSTA: Pode colocar

---

### 3.5 Configurações

- 🟠 **Senha do SQL gravada em texto puro** em `%AppData%\BuscaBT\connection.settings.json`. Criptografar com DPAPI (`ProtectedData`, escopo do usuário).
- 🟡 Adicionar a **pasta central de templates** (ver 3.3) e a **pasta dos relatórios**.
- ⚪ Mostrar o servidor efetivamente conectado (a descoberta automática pode ter trocado) e a versão do app.

RESPOSTA: Não precisa, *projeto Plus BT 2.0
---

### 3.6 Ajuda, identidade e geral

- ⚪ A versão aparece fixa como **"1.0.0"** (`HelpView.xaml:33`). Ler do assembly.
- ⚪ O nome muda de tela para tela: "PlusBT — Sistema de Busca de Etiquetas", "Gestão de Etiquetas BarTender", "BarTender Plus", "Lista de Invoices". Padronizar.
- ⚪ A barra lateral não indica a tela atual. Destacar o item ativo.
- 🟡 Avisos misturados: alguns são `MessageBox` do Windows, outros são modais próprios (ciência, limpar fila, resumo). Padronizar nos modais próprios, que são mais bonitos e não somem atrás de outras janelas.
- 🟡 **Guia rápido na Ajuda** para o operador: importar, achar, abrir, imprimir, e o que fazer em cada pendência.
RESPOSTA: Pode colocar

---

### 3.7 Rastreabilidade (log)

- 🟠 **Hoje:** o log só tem saída para o console (`App.xaml.cs:157`). Num app de janela o console não existe, então **nada fica registrado**: nem erros, nem a confirmação de ciência de pendência, nem quem abriu o quê.
- **Sugestão:**
  1. Gravar o log em arquivo diário (`%ProgramData%\BuscaBT\logs\`, com rotação de 30 dias).
  2. Para auditoria, criar uma tabela **`dbo.Eventos`** (data, PC, usuário do Windows, ação, invoice, código, detalhes) e gravar nela: importação, abertura, ciência de pendência, limpeza de fila e troca de template. Isso permite responder depois *"quem imprimiu a etiqueta com validade errada?"*.
RESPOSTA: Não precisa, *projeto Plus BT 2.0
---

## 4. Fluxos propostos

### 4.1 Importação sem perder trabalho
```
[Importar] ou arrastar o .xlsx
   → fila vazia?         → importa direto
   → fila com itens?     → "Adicionar à fila" (padrão) | "Substituir tudo" | Cancelar
   → mesma planilha já importada hoje? → aviso
   → resumo: ✔ importados · ⚠ pendências agrupadas por tipo [Ver na fila] · ✖ ignorados
```

### 4.2 Bipar e imprimir (leitor de código de barras)
```
foco na busca → bipa o LPN → Enter
   → 1 item  → (ciência, se houver pendência) → abre no BarTender → [Marcar impresso]
   → n itens → filtra; ↑/↓ escolhe; Enter abre
   → 0 itens → "LPN não está na fila" (som de erro opcional)
```

### 4.3 Resolver "sem template" sem trocar de tela
```
item ⚠ "Sem template" → [Ver motivo] → [Vincular arquivo agora]
   → seletor já na pasta central, sugerindo <código>.btw
   → copia para a pasta central → vincula → item já fica pronto para abrir
```

---

## 5. Plano sugerido

**Fase 1: correções rápidas (1–2 dias)**
- Corrigir o "Fechar" da tela Templates (3.3).
- Colunas Validade e Qtd na Home (3.1).
- Botão Atualizar / F5 na Home (3.1).
- Log em arquivo (3.7).
- Versão lida do assembly e nome padronizado (3.6).
- Mensagem errada em "Importar arquivo" (3.3).

**Fase 2: fluxo do dia a dia (1 semana)**
- Importar adicionando à fila, em vez de substituir (3.1).
- Pasta central de templates e bloqueio de caminho local (3.3).
- Modo leitor de código de barras e atalhos de teclado (3.1).
- Arrastar e soltar a planilha (3.2).
- Pendências agrupadas no resumo, com "Ver na fila" (3.2).
- "Vincular arquivo agora" a partir do item sem template (3.3 / 4.3).

**Fase 3: controle e evolução**
- Estados Pendente → Aberto → Impresso, com desfazer (3.1).
- Tabela de eventos para auditoria (3.7).
- Detalhes da importação no Histórico e quem importou (3.4).
- Senha criptografada (3.5).
- Impressão direta com dados preenchidos pelo sistema (3.1).

---

## 6. Perguntas para validar com os operadores

1. Depois de abrir o `.btw`, **vocês digitam lote, validade e quantidade no BarTender**, ou o template já puxa os dados? *(Define se a impressão direta é prioridade.)*
Digitamos manualmente
2. **Quantas planilhas** chegam por dia? Elas se acumulam na fila ou cada uma é finalizada antes da próxima?
Não tem media, eles vão começar a mandar uam por semana
3. **Mais de um PC** usa o sistema ao mesmo tempo? Cada um deveria ter a sua fila ou a fila é da equipe?
Só um único pc realmente imprime, mas a atual~ização dos aruqivos e envio de planilha é em outro pc
4. Vocês usam **leitor de código de barras**? Ele bipa o LPN, o código ou outra coisa?
Não
5. Depois de imprimir, alguém **confere** as etiquetas contra o relatório? *(Define se vale a pena um modo de conferência.)*
Sim
6. Quem **cadastra templates novos**: o próprio operador ou outra pessoa?
Outra pessoa em outro computador