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
#>
param([switch]$SoLocal)

$ErrorActionPreference = 'Stop'
$repoUrl = 'https://github.com/TheoStracke/PlusBT'
$raiz = $PSScriptRoot
$projeto = Join-Path $raiz 'Busca_BT\Busca_BT.csproj'
$publish = Join-Path $raiz 'Busca_BT\bin\Publish'
$releases = Join-Path $raiz 'Releases'

$versao = ([xml](Get-Content $projeto -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $versao) { throw 'Não achei <Version> no Busca_BT.csproj.' }
Write-Host "Versão $versao" -ForegroundColor Cyan

$token = $env:GITHUB_TOKEN
$envFile = Join-Path $raiz '.env'
if (-not $token -and (Test-Path $envFile)) {
    $linha = Get-Content $envFile | Where-Object { $_ -match '^\s*GITHUB_TOKEN\s*=' } | Select-Object -First 1
    if ($linha) { $token = ($linha -split '=', 2)[1].Trim() }
}
if (-not $SoLocal -and -not $token) { throw 'Falta o GITHUB_TOKEN (variável de ambiente ou .env).' }

# 1. Publica o app (perfil FolderProfile: win-x64, autocontido, arquivo único).
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $projeto -c Release -p:PublishProfile=FolderProfile -p:PublishDir="$publish\"
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falhou.' }

# 2. Baixa a última release para o vpk gerar o pacote delta (atualização menor).
New-Item -ItemType Directory -Force $releases | Out-Null
$dlArgs = @('download', 'github', '--repoUrl', $repoUrl, '-o', $releases)
if ($token) { $dlArgs += @('--token', $token) }
vpk @dlArgs
if ($LASTEXITCODE -ne 0) { Write-Host 'Sem release anterior: gera só o pacote completo.' -ForegroundColor Yellow }

# 3. Empacota (o id Busca_BT é o mesmo das instalações já existentes).
vpk pack -u Busca_BT -v $versao -p $publish -e Busca_BT.exe -o $releases `
    --packTitle 'PlusBT' -i (Join-Path $raiz 'Busca_BT\favicon.ico')
if ($LASTEXITCODE -ne 0) { throw 'vpk pack falhou.' }

if ($SoLocal) {
    Write-Host "Pacotes em $releases (Busca_BT-win-Setup.exe instala neste PC)." -ForegroundColor Green
    return
}

# 4. Publica no GitHub Releases.
vpk upload github -o $releases --repoUrl $repoUrl --token $token --publish `
    --releaseName "PlusBT $versao" --tag "v$versao"
if ($LASTEXITCODE -ne 0) { throw 'vpk upload falhou.' }

Write-Host "Versão $versao publicada em $repoUrl/releases" -ForegroundColor Green
