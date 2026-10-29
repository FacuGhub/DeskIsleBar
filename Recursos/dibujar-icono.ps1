# Dibuja el icono de DeskIsleBar: cuadrado redondeado con degrade + la pildora negra de la isla con barritas de musica.
# Genera PNG en cada tamaño y un .ico con todos (entradas PNG, como los iconos modernos de Windows).
param([string]$Salida = (Split-Path $MyInvocation.MyCommand.Path))
Add-Type -AssemblyName System.Drawing

function Redondeado([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}

function Dibujar([int]$t) {
    $bmp = New-Object Drawing.Bitmap $t, $t, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([Drawing.Color]::Transparent)

    # Fondo: cuadrado redondeado, degrade azul (arriba) a violeta (abajo)
    $m = $t * 0.04
    $fondo = Redondeado $m $m ($t - 2 * $m) ($t - 2 * $m) ($t * 0.22)
    $deg = New-Object Drawing.Drawing2D.LinearGradientBrush ((New-Object Drawing.PointF 0, 0), (New-Object Drawing.PointF $t, $t),
        [Drawing.Color]::FromArgb(255, 10, 132, 255), [Drawing.Color]::FromArgb(255, 125, 70, 230))
    $g.FillPath($deg, $fondo)

    # La isla: pildora negra, ancha, un poco arriba del centro (como queda en la pantalla)
    $pw = $t * 0.70; $ph = $t * 0.27
    $px = ($t - $pw) / 2; $py = $t * 0.30
    $pildora = Redondeado $px $py $pw $ph ($ph / 2)
    $g.FillPath([Drawing.Brushes]::Black, $pildora)

    # Adentro: punto (portada) a la izquierda y barritas verdes a la derecha; en los tamaños chicos solo la pildora
    if ($t -ge 32) {
        $verde = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 48, 209, 88))
        $blanco = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(235, 255, 255, 255))
        $dot = $ph * 0.46
        $g.FillEllipse($blanco, $px + $ph * 0.30, $py + ($ph - $dot) / 2, $dot, $dot)
        $altos = @(0.42, 0.70, 0.52, 0.62)
        $bw = $ph * 0.11; $gap = $bw * 0.75
        $x0 = $px + $pw - $ph * 0.32 - 4 * $bw - 3 * $gap
        for ($i = 0; $i -lt 4; $i++) {
            $bh = $ph * $altos[$i]
            $barra = Redondeado ($x0 + $i * ($bw + $gap)) ($py + ($ph - $bh) / 2) $bw $bh ($bw / 2)
            $g.FillPath($verde, $barra)
        }
    }
    # Debajo de la isla, una linea tenue: la "pantalla" del escritorio (le da contexto al ojo)
    if ($t -ge 48) {
        $tenue = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(70, 255, 255, 255))
        $g.FillPath($tenue, (Redondeado ($t * 0.22) ($t * 0.70) ($t * 0.56) ($t * 0.07) ($t * 0.035)))
    }
    $g.Dispose()
    return $bmp
}

$medidas = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$pngs = @{}
foreach ($t in $medidas) {
    $bmp = Dibujar $t
    $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $pngs[$t] = $ms.ToArray()
    $bmp.Save((Join-Path $Salida "icono-$t.png"), [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}

# .ico: cabecera + directorio + imagenes PNG
$ico = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$medidas.Count)
$offset = 6 + 16 * $medidas.Count
foreach ($t in $medidas) {
    $lado = if ($t -ge 256) { 0 } else { $t }
    $w.Write([byte]$lado); $w.Write([byte]$lado); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$t].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$t].Length
}
foreach ($t in $medidas) { $w.Write($pngs[$t]) }
$w.Flush(); [IO.File]::WriteAllBytes((Join-Path $Salida 'DeskIsleBar.ico'), $ico.ToArray())

# Vista previa: los tamaños uno al lado del otro, sobre fondo claro y oscuro
$prev = New-Object Drawing.Bitmap 760, 300; $g = [Drawing.Graphics]::FromImage($prev)
$g.FillRectangle((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 243, 243, 243))), 0, 0, 760, 150)
$g.FillRectangle((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 32, 32, 32))), 0, 150, 760, 150)
foreach ($fila in 0, 150) {
    $x = 10
    foreach ($t in 16, 24, 32, 48, 64, 128) {
        $img = [Drawing.Image]::FromStream((New-Object IO.MemoryStream (, $pngs[$t])))
        $g.DrawImage($img, $x, $fila + (150 - $t) / 2, $t, $t); $x += $t + 30
    }
}
$g.Dispose(); $prev.Save((Join-Path $Salida 'vista-previa.png'), [Drawing.Imaging.ImageFormat]::Png)
"listo: $Salida"
