# Arma el instalador: publica la app con .NET incluido (no hace falta instalarlo aparte) y compila el .iss.
# Uso: .\Instalador\armar.ps1   (el resultado queda en bin\instalador\DeskIsleBar-Setup-<version>.exe)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) { $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
if (-not (Test-Path $iscc)) { throw 'No se encontro Inno Setup 6 (winget install JRSoftware.InnoSetup)' }

[xml]$proyecto = Get-Content "$raiz\DeskIsleBar.csproj"
$version = ($proyecto.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { $version = '1.0.0' }

$publicado = "$raiz\bin\publicar"
if (Test-Path $publicado) { Remove-Item $publicado -Recurse -Force }
# Sin informacion de depuracion: si no, el .dll guarda la ruta del .pdb, que incluye el usuario de Windows de
# quien compila (C:\Users\<usuario>\...). Al final se verifica que no haya quedado ninguna ruta de la PC.
& $dotnet publish "$raiz\DeskIsleBar.csproj" -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true `
    -p:DebugType=None -p:DebugSymbols=false -o $publicado
if ($LASTEXITCODE -ne 0) { throw 'fallo dotnet publish' }
Remove-Item "$publicado\*.pdb" -ErrorAction SilentlyContinue

$usuario = [Text.Encoding]::ASCII.GetBytes($env:USERNAME)
foreach ($archivo in Get-ChildItem $publicado -Recurse -File) {
    $datos = [IO.File]::ReadAllBytes($archivo.FullName)
    $texto = [Text.Encoding]::ASCII.GetString($datos)
    if ($texto.IndexOf("Users\$env:USERNAME", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "El archivo $($archivo.Name) contiene la ruta del usuario ($env:USERNAME): no se arma el instalador"
    }
}

& $iscc "/DVersion=$version" "$PSScriptRoot\DeskIsleBar.iss"
if ($LASTEXITCODE -ne 0) { throw 'fallo Inno Setup' }
Get-Item "$raiz\bin\instalador\DeskIsleBar-Setup-$version.exe" | Select-Object Name, @{ n = 'MB'; e = { [Math]::Round($_.Length / 1MB, 1) } }
