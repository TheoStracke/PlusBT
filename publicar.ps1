<#
  Gera uma versão nova do PlusBT e publica no GitHub Releases. Os PCs com o app
  instalado baixam sozinhos (Velopack) e instalam ao reiniciar o app.

  Antes de rodar:
    1. Suba o <Version> em Busca_BT\Busca_BT.csproj (ex.: 3.1.0 -> 3.1.1).
    2. Tenha um token do GitHub com permissão de escrita em "Contents" no repositório
       PlusBT, em $env:GITHUB_TOKEN ou numa linha GITHUB_TOKEN=... no .env (que não
       vai para o Git).

  Uso:
    .\publicar.ps1             # gera e publica
    .\publicar.ps1 -SoLocal    # só gera os pacotes em .\Releases (para testar o Setup)
    .\publicar.ps1 -SoLocal -Versao 3.0.9 -Saida Releases-teste
                               # Setup de uma versão mais antiga, para testar a atualização
#>
param(
    [switch]$SoLocal,
    [string]$Versao,             # sobrescreve o <Version> do .csproj
    [string]$Saida = 'Releases'  # pasta dos pacotes (relativa à raiz do repo)
)

$ErrorActionPreference = 'Stop'
$repoUrl = 'https://github.com/TheoStracke/PlusBT'
$raiz = $PSScriptRoot
$projeto = Join-Path $raiz 'Busca_BT\Busca_BT.csproj'
$publish = Join-Path $raiz 'Busca_BT\bin\Publish'
$releases = Join-Path $raiz $Saida
# A pasta de saída é apagada a cada execução: só aceita nomes Releases / Releases-*.
if ((Split-Path $releases -Leaf) -notmatch '^Releases(-.+)?$') { throw "-Saida precisa se chamar Releases ou Releases-algo." }

$versao = $Versao
if (-not $versao) {
    $versao = ([xml](Get-Content $projeto -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if (-not $versao) { throw 'Não achei <Version> no Busca_BT.csproj.' }
Write-Host "Versão $versao" -ForegroundColor Cyan

$envFile = Join-Path $raiz '.env'
function LerEnv([string]$nome) {
    $valor = [Environment]::GetEnvironmentVariable($nome)
    if (-not $valor -and (Test-Path $envFile)) {
        $linha = Get-Content $envFile | Where-Object { $_ -match "^\s*$nome\s*=" } | Select-Object -First 1
        if ($linha) { $valor = ($linha -split '=', 2)[1].Trim() }
    }
    return $valor
}

$token = LerEnv 'GITHUB_TOKEN'
if (-not $SoLocal -and -not $token) { throw 'Falta o GITHUB_TOKEN (variável de ambiente ou .env).' }

# Conexão padrão embutida no .exe: o app instalado conecta no Supabase sem ninguém digitar
# nada. Usa o plusbt_app (acesso só ao schema plusbt), nunca o postgres.
$appUser = LerEnv 'PLUSBT_APP_DB_USER'
$appSenha = LerEnv 'PLUSBT_APP_DB_PASSWORD'
if (-not $appUser -or -not $appSenha) {
    if (-not $SoLocal) { throw 'Falta PLUSBT_APP_DB_USER / PLUSBT_APP_DB_PASSWORD no .env.' }
    Write-Host 'Sem PLUSBT_APP_DB_* no .env: o app gerado não terá a conexão padrão.' -ForegroundColor Yellow
}
$conexaoBin = Join-Path $raiz 'Busca_BT\conexao.bin'

# 1. Publica o app (perfil FolderProfile: win-x64, autocontido, arquivo único).
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
try {
    if ($appUser -and $appSenha) {
        $porta = LerEnv 'SUPABASE_DB_PORT'
        $json = [ordered]@{
            Host     = $(if ($h = LerEnv 'SUPABASE_DB_HOST') { $h } else { 'aws-0-sa-east-1.pooler.supabase.com' })
            Port     = $(if ($porta) { [int]$porta } else { 5432 })
            Database = $(if ($d = LerEnv 'SUPABASE_DB_NAME') { $d } else { 'postgres' })
            Username = $appUser
            Password = $appSenha
        } | ConvertTo-Json -Compress
        # Mesmo embaralhamento (XOR) que o ConnectionSettingsStore desfaz ao ler.
        $chave = [Text.Encoding]::UTF8.GetBytes('PlusBT.conexao.v1')
        $bytes = [Text.Encoding]::UTF8.GetBytes($json)
        for ($i = 0; $i -lt $bytes.Length; $i++) { $bytes[$i] = $bytes[$i] -bxor $chave[$i % $chave.Length] }
        [IO.File]::WriteAllText($conexaoBin, [Convert]::ToBase64String($bytes))
    }

    dotnet publish $projeto -c Release -p:PublishProfile=FolderProfile -p:PublishDir="$publish\" -p:Version=$versao
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falhou.' }
}
finally {
    # Só existe durante o publish: não fica solto no repositório.
    if (Test-Path $conexaoBin) { Remove-Item $conexaoBin -Force }
}

# 2. Baixa a última release para o vpk gerar o pacote delta (atualização menor).
# A pasta é sempre gerada do zero: a release anterior vem do GitHub logo abaixo.
if (Test-Path $releases) { Remove-Item $releases -Recurse -Force }
New-Item -ItemType Directory -Force $releases | Out-Null
$dlArgs = @('download', 'github', '--repoUrl', $repoUrl, '-o', $releases)
if ($token) { $dlArgs += @('--token', $token) }
# Com -Versao (ex.: versão antiga para teste) não baixa: o vpk recusa empacotar uma
# versão menor que a da release baixada.
if (-not $Versao) {
    vpk @dlArgs
    if ($LASTEXITCODE -ne 0) { Write-Host 'Sem release anterior: gera só o pacote completo.' -ForegroundColor Yellow }
}

# 3. Empacota (o id Busca_BT é o mesmo das instalações já existentes).
vpk pack -u Busca_BT -v $versao -p $publish -e Busca_BT.exe -o $releases `
    --packTitle 'PlusBT' -i (Join-Path $raiz 'Busca_BT\favicon.ico')
if ($LASTEXITCODE -ne 0) { throw 'vpk pack falhou.' }

if ($SoLocal) {
    Write-Host "Pacotes em $releases (Busca_BT-win-Setup.exe instala neste PC)." -ForegroundColor Green
    return
}

# 4. Publica no GitHub Releases. A tag aponta para o commit atual, que precisa já estar
#    no GitHub (git push) para a tag ser criada nele.
$commit = (git -C $raiz rev-parse HEAD).Trim()
vpk upload github -o $releases --repoUrl $repoUrl --token $token --publish `
    --releaseName "PlusBT $versao" --tag "v$versao" --targetCommitish $commit
if ($LASTEXITCODE -ne 0) { throw 'vpk upload falhou.' }

Write-Host "Versão $versao publicada em $repoUrl/releases" -ForegroundColor Green
