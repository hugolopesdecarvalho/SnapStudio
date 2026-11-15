# Compila o SnapStudio com o csc.exe do .NET Framework 4.x (não precisa de .NET SDK).
# Uso:  powershell -ExecutionPolicy Bypass -File build.ps1 [-Run]
param([switch]$Run)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$bin  = Join-Path $root 'bin'
$exe  = Join-Path $bin 'SnapStudio.exe'
$ico  = Join-Path $root 'assets\app.ico'
$man  = Join-Path $root 'app.manifest'
$src  = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName })

New-Item -ItemType Directory -Force $bin | Out-Null

$running = Get-Process SnapStudio -ErrorAction SilentlyContinue
if ($running) { Write-Host 'A fechar o SnapStudio em execução...'; $running | Stop-Process -Force; Start-Sleep -Milliseconds 500 }

function Invoke-Compile([bool]$withIcon) {
    $a = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', '/utf8output',
           "/out:$exe", "/win32manifest:$man",
           '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
           '/r:System.Runtime.Serialization.dll', '/r:System.Xml.dll', '/r:Microsoft.VisualBasic.dll')
    if ($withIcon) { $a += "/win32icon:$ico" }
    $a += $src
    & $csc @a
    if ($LASTEXITCODE -ne 0) { throw "Falha na compilação ($LASTEXITCODE)" }
}

if (-not (Test-Path $ico)) {
    Write-Host 'A gerar o ícone...'
    Invoke-Compile $false
    New-Item -ItemType Directory -Force (Split-Path $ico) | Out-Null
    Start-Process -FilePath $exe -ArgumentList @('--make-icon', "`"$ico`"") -Wait
}

Invoke-Compile $true
Write-Host "OK -> $exe"

# ficheiro para a release: um zip passa nos browsers sem o aviso que um .exe solto recebe
$zip = Join-Path $bin 'SnapStudio-Windows.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $exe -DestinationPath $zip
Write-Host "OK -> $zip"
if ($Run) { Start-Process $exe }
