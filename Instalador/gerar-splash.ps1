<#
  Gera Instalador\splash-instalador.png: a imagem que o Busca_BT-win-Setup.exe mostra
  enquanto instala (o Velopack desenha a barra de progresso por cima, embaixo).
  Rodar de novo só se mudar o visual:  .\Instalador\gerar-splash.ps1
#>
Add-Type -AssemblyName System.Drawing

$raiz = Split-Path $PSScriptRoot -Parent
$saida = Join-Path $PSScriptRoot 'splash-instalador.png'
$logo = [System.Drawing.Image]::FromFile((Join-Path $raiz 'Busca_BT\BT+.png'))

$w = 520; $h = 320
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'AntiAliasGridFit'
$g.InterpolationMode = 'HighQualityBicubic'

# Fundo claro com uma faixa na cor do logo à esquerda.
$azul = [System.Drawing.ColorTranslator]::FromHtml('#008DBC')
$laranja = [System.Drawing.ColorTranslator]::FromHtml('#DE900C')
$g.Clear([System.Drawing.ColorTranslator]::FromHtml('#FAFAFA'))
$grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0, 0)), ([System.Drawing.Point]::new(0, $h)), $azul, ([System.Drawing.ColorTranslator]::FromHtml('#005F80'))
$g.FillRectangle($grad, 0, 0, 170, $h)
$g.FillRectangle((New-Object System.Drawing.SolidBrush $laranja), 170, 0, 4, $h)

# Logo num cartão branco dentro da faixa.
$g.FillEllipse([System.Drawing.Brushes]::White, 33, 108, 104, 104)
$g.DrawImage($logo, 45, 120, 80, 80)

# Textos.
$fonte = 'Segoe UI'
$escuro = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#1B1B1B'))
$cinza = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#616161'))
$g.DrawString('PlusBT', (New-Object System.Drawing.Font $fonte, 30, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)), $escuro, 200, 96)
$g.DrawString('Sistema de Busca de Etiquetas', (New-Object System.Drawing.Font "$fonte Semilight", 15, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)), $cinza, 202, 140)
$g.FillRectangle((New-Object System.Drawing.SolidBrush $laranja), 204, 172, 36, 3)
$g.DrawString('Instalando… o PlusBT abre sozinho ao terminar.', (New-Object System.Drawing.Font $fonte, 12, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)), $cinza, 202, 190)
$g.DrawString('TS Builds', (New-Object System.Drawing.Font $fonte, 11, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)), $cinza, 202, 278)

# Borda fina.
$g.DrawRectangle((New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml('#D0D0D0'))), 0, 0, $w - 1, $h - 1)

$bmp.Save($saida, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose(); $logo.Dispose()
Write-Host "Gerado: $saida"
