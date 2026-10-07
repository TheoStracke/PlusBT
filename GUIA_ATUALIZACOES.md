# PlusBT: guia de atualizações pela internet (Velopack + GitHub Releases)

> Escrito em 07/10/2026, quando a atualização automática foi implementada (versão 3.1.0).

## Contexto

O PlusBT roda em **dois PCs** (impressão e administração). Antes, cada versão nova precisava ser copiada e instalada à mão em cada PC. Agora:

- Você gera a versão nova **uma vez**, no seu PC, e publica no **GitHub Releases** do repositório `TheoStracke/PlusBT`.
- Cada PC com o app instalado **procura sozinho** a versão nova (ao abrir e a cada 4 horas), **baixa em segundo plano** e mostra na barra lateral o aviso **"Versão X pronta"**, com o botão **Reiniciar e atualizar**.
- Se ninguém clicar, a versão nova é instalada **na próxima vez que o app abrir**. O PC de impressão nunca é interrompido no meio do trabalho.
- Os dados (fila, templates, operadores) ficam no **Supabase**, então os dois PCs veem a mesma coisa. A atualização troca **só o programa**, não mexe nos dados.

```
  Seu PC (Visual Studio)                GitHub Releases                 PCs da operação
  ──────────────────────               ─────────────────               ─────────────────
  sobe <Version> no .csproj   ──►     PlusBT 3.1.1        ◄── procura ── PC IMPRESSÃO
  roda .\publicar.ps1                  (Setup + pacotes)   ◄── procura ── PC ADMINISTRAÇÃO
                                                              baixa e instala ao reiniciar
```

### Peças envolvidas

| Peça | O que é |
|---|---|
| **Velopack** | Biblioteca de instalação e atualização. O pacote NuGet `Velopack` fica dentro do app; a ferramenta `vpk` (instalada com `dotnet tool install -g vpk`) gera os pacotes. As duas estão na **1.2.0**: mantenha as versões iguais. |
| `Busca_BT/Program.cs` | Ponto de entrada do app. Chama `VelopackApp.Build().Run()` antes de qualquer janela. É isso que faz a instalação funcionar e aplica uma atualização já baixada. |
| `Busca_BT/Services/AtualizacaoService.cs` | Procura e baixa as atualizações no GitHub, e mostra o aviso. |
| `<Version>` no `Busca_BT/Busca_BT.csproj` | **A versão do app.** É o número que os PCs comparam para saber se existe versão nova. |
| `publicar.ps1` (raiz do repo) | Script que compila, empacota e publica. |
| Pasta `Releases/` | Onde o script deixa os pacotes. Não vai para o Git (`.gitignore`), porque os arquivos têm ~70 MB. |

---

## Configuração (uma vez só)

### 1. Token do GitHub (só no seu PC)

O script precisa de permissão para criar releases no repositório. Os PCs da operação **não** precisam de token, porque o repositório é público.

1. GitHub → foto do perfil → **Settings → Developer settings → Personal access tokens → Fine-grained tokens → Generate new token**.
2. **Repository access:** *Only select repositories* → `PlusBT`.
3. **Permissions → Repository permissions → Contents:** *Read and write*.
4. Copie o token e coloque numa linha nova do `.env` na raiz do repositório:
   ```
   GITHUB_TOKEN=github_pat_...
   ```

> ⚠️ O token fica **só no `.env`**, que é ignorado pelo Git. **Nunca** coloque o token neste arquivo, no `.env.example` ou num commit. Se ele vazar, apague o token no GitHub e gere outro.

### 2. Instalar a versão nova em cada PC

A versão **3.0.0**, instalada hoje nos PCs, é anterior a isso e **não procura atualizações**. Por isso, é preciso instalar à mão **uma última vez**:

1. Publique a 3.1.0 (veja a próxima seção) ou rode `.\publicar.ps1 -SoLocal`.
2. Copie `Releases\Busca_BT-win-Setup.exe` para cada PC e execute.
3. O Setup instala por cima da 3.0.0, porque o id é o mesmo (`Busca_BT`). O atalho que já existe continua funcionando.
4. Confira no **rodapé da barra lateral** se aparece **"Versão 3.1.0"**.

A partir daí, as próximas versões chegam sozinhas.

---

## Publicar uma versão nova (o dia a dia)

1. Faça e teste as mudanças no Visual Studio, como sempre.
2. **Suba o número da versão** em `Busca_BT/Busca_BT.csproj`:
   ```xml
   <Version>3.1.1</Version>
   ```
   Regra prática: **3.1.1** para correção · **3.2.0** para funcionalidade nova · **4.0.0** para mudança grande.
   O número **precisa ser maior** que o da última release. Com um número igual ou menor, os PCs não atualizam.
3. Faça o commit.
4. No PowerShell, na raiz do repositório:
   ```powershell
   .\publicar.ps1
   ```
   O script:
   1. compila o app (`dotnet publish`, perfil `FolderProfile`);
   2. baixa a release anterior do GitHub para gerar um **pacote delta** (só a diferença, então a atualização é menor);
   3. empacota com `vpk pack`;
   4. publica no GitHub com `vpk upload github`, com a tag `v3.1.1`.
5. Confira em https://github.com/TheoStracke/PlusBT/releases.
6. Em até 4 horas (ou ao reabrir o app), cada PC mostra **"Versão 3.1.1 pronta"**.

> 💡 Se aparecer `Não foi possível executar scripts nesse sistema`, rode antes:
> `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`

### Testar antes de publicar (opcional)

Para ver a atualização funcionando sem mandar nada para os PCs:

1. `.\publicar.ps1 -SoLocal` gera os pacotes só na pasta `Releases\`.
2. Instale ou extraia uma versão antiga (por exemplo, o `Busca_BT-win-Portable.zip` de uma versão anterior).
3. Abra essa versão com a variável que troca o GitHub por uma pasta:
   ```powershell
   $env:PLUSBT_ATUALIZACAO_ORIGEM = "C:\caminho\para\Releases"
   & "C:\caminho\do\app\PlusBT.exe"
   ```
4. Em cerca de 15 s aparece o aviso. **Reiniciar e atualizar** fecha o app e o abre na versão nova.

---

## Problemas comuns

| Sintoma | Causa provável / o que fazer |
|---|---|
| O PC não mostra o aviso | A versão instalada é a 3.0.0 (veja o rodapé): instale o Setup novo uma vez. Ou o `<Version>` não foi aumentado. Ou o PC está sem internet (ele tenta de novo a cada 4 h). |
| `Falta o GITHUB_TOKEN` | Falta a linha `GITHUB_TOKEN=` no `.env`. |
| `vpk upload falhou` com 401/403 | Token vencido ou sem permissão *Contents: Read and write* no PlusBT. |
| `vpk upload falhou` dizendo que a release já existe | Essa versão já foi publicada: aumente o `<Version>`. |
| Rodando pelo Visual Studio aparece "Versão dev" | Normal: só o app instalado pelo Setup se atualiza. |
| Aviso `There is a newer version of vpk available` | Pode ignorar. Se atualizar o `vpk`, atualize também o pacote `Velopack` no `.csproj` para a mesma versão. |
| "Conexão indisponível" ao abrir | Não é a atualização: é o Supabase. Confira se o projeto não foi **pausado** no painel (o plano gratuito pausa projetos parados) e a conexão em **Configurações**. |

## Situação em 07/10/2026

- ✅ Atualização automática implementada e testada (3.1.0 → 3.1.1, com pacote delta).
- ⬜ Criar o token do GitHub e pôr no `.env`.
- ⬜ **Supabase fora do ar**: o endereço do projeto não responde (provavelmente **pausado**). Restaurar no painel e conferir a connection string (*Project Settings → Database → Session pooler*).
- ⬜ Etapa 5 do [`PLANO_MIGRACAO_SUPABASE.md`](PLANO_MIGRACAO_SUPABASE.md): carga inicial dos 880 `.btw`, feita por você em **Templates → Importar pasta**.
- ⬜ Mesclar a branch `feat/pendencias-etiquetas` na `main`, publicar a 3.1.0 e instalar o Setup nos dois PCs.
