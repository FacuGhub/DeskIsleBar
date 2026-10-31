# Arma el paquete MSIX para la Microsoft Store (la Store lo firma al publicarlo).
# Uso: .\Store\armar-msix.ps1 -Herramientas <carpeta con makeappx.exe y makepri.exe>
#   (vienen en el Windows SDK o en el paquete NuGet oficial Microsoft.Windows.SDK.BuildTools, carpeta bin\<version>\x64)
# Resultado: bin\msix\DeskIsleBar-<version>.msix y la carpeta bin\msix\layout (sirve para probarlo sin firmar).
param([Parameter(Mandatory)][string]$Herramientas)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$makeappx = Join-Path $Herramientas 'makeappx.exe'; $makepri = Join-Path $Herramientas 'makepri.exe'
foreach ($h in $makeappx, $makepri) { if (-not (Test-Path $h)) { throw "No se encontro $h" } }

[xml]$proyecto = Get-Content "$raiz\DeskIsleBar.csproj"
$version = ($proyecto.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
$versionPaquete = "$version.0"   # la Store pide 4 numeros y el ultimo en 0

$salida = "$raiz\bin\msix"; $layout = "$salida\layout"
if (Test-Path $salida) { Remove-Item $salida -Recurse -Force }
New-Item -ItemType Directory -Force $layout | Out-Null

# La misma publicacion que el instalador: con .NET incluido y sin informacion de depuracion (no guarda rutas de la PC)
& $dotnet publish "$raiz\DeskIsleBar.csproj" -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true `
    -p:DebugType=None -p:DebugSymbols=false -o $layout
if ($LASTEXITCODE -ne 0) { throw 'fallo dotnet publish' }
Remove-Item "$layout\*.pdb" -ErrorAction SilentlyContinue

Copy-Item "$PSScriptRoot\Assets" "$layout\Assets" -Recurse
(Get-Content "$PSScriptRoot\AppxManifest.xml" -Raw).Replace('{VERSION}', $versionPaquete) |
    Set-Content "$layout\AppxManifest.xml" -Encoding UTF8

# Indice de recursos: para que Windows elija el icono justo segun la escala y el tamaño
Push-Location $layout
& $makepri createconfig /cf "$salida\priconfig.xml" /dq en-US_es-ES /o | Out-Null
& $makepri new /pr $layout /cf "$salida\priconfig.xml" /mn "$layout\AppxManifest.xml" /of "$layout\resources.pri" /o | Out-Null
Pop-Location
if ($LASTEXITCODE -ne 0) { throw 'fallo makepri' }

# Nada con la ruta de la PC de quien compila
foreach ($archivo in Get-ChildItem $layout -Recurse -File) {
    $texto = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($archivo.FullName))
    if ($texto.IndexOf("Users\$env:USERNAME", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "El archivo $($archivo.Name) contiene la ruta del usuario ($env:USERNAME): no se arma el paquete"
    }
}

& $makeappx pack /d $layout /p "$salida\DeskIsleBar-$versionPaquete.msix" /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'fallo makeappx' }
Get-Item "$salida\DeskIsleBar-$versionPaquete.msix" | Select-Object Name, @{ n = 'MB'; e = { [Math]::Round($_.Length / 1MB, 1) } }
