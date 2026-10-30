using System.IO;
using System.Runtime.InteropServices;

namespace DeskIsleBar.Modulos;

public record AnalisisLimpieza(long BytesTemporales, int ArchivosTemporales, long BytesPapelera, long ElementosPapelera)
{
    public long Total => BytesTemporales + BytesPapelera;
}

public record ResultadoLimpieza(long BytesLiberados, int ArchivosSalteados);

/// <summary>
/// Libera espacio: archivos temporales del usuario (%TEMP%) con mas de 1 dia y la papelera de reciclaje.
/// No toca C:\Windows\Temp (requiere admin). Los archivos en uso se saltean. No sigue enlaces/junctions,
/// asi que no puede borrar nada fuera de %TEMP%. Es irreversible: la interfaz pide confirmacion antes.
/// </summary>
public static class Limpieza
{
    /// <summary>Los temporales recientes suelen ser de programas abiertos: borrarlos puede romperlos.</summary>
    private static readonly TimeSpan Antiguedad = TimeSpan.FromDays(1);

    private static readonly EnumerationOptions Recorrido = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,   // no seguir enlaces fuera de %TEMP%
    };

    [StructLayout(LayoutKind.Sequential)]   // sin Pack: en 64 bits shellapi.h usa el empaquetado por defecto
    private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? raiz, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? raiz, uint flags);

    private const uint SHERB_NOCONFIRMATION = 0x1, SHERB_NOPROGRESSUI = 0x2, SHERB_NOSOUND = 0x4;

    /// <summary>
    /// La carpeta de temporales, o null si no parece una. Windows la toma de TMP/TEMP y, si faltan, cae al perfil
    /// del usuario o a C:\Windows: si alguien puso TMP=D:\ o la variable no existe, "limpiar temporales" borraria
    /// sus archivos de verdad. Solo se acepta una carpeta llamada Temp/Tmp que no sea la raiz de un disco ni este
    /// dentro de Windows.
    /// </summary>
    private static string? CarpetaTemporal
    {
        get
        {
            string ruta = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            string nombre = Path.GetFileName(ruta);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            bool pareceTemporal = nombre.Equals("Temp", StringComparison.OrdinalIgnoreCase) || nombre.Equals("Tmp", StringComparison.OrdinalIgnoreCase);
            bool esRaiz = string.Equals(Path.GetPathRoot(ruta)?.TrimEnd('\\'), ruta, StringComparison.OrdinalIgnoreCase);
            bool dentroDeWindows = windows.Length > 0 && ruta.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase);
            if (pareceTemporal && !esRaiz && !dentroDeWindows) return ruta;
            Log.Escribir("limpieza: la carpeta de temporales no parece una carpeta Temp, no se toca");
            return null;
        }
    }

    /// <summary>Solo mide, no borra nada.</summary>
    public static AnalisisLimpieza Analizar()
    {
        long bytes = 0;
        int archivos = 0;
        foreach (var archivo in TemporalesViejos())
        {
            bytes += archivo.Length;
            archivos++;
        }

        var papelera = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        if (SHQueryRecycleBin(null, ref papelera) != 0) papelera = default;   // sin papelera o sin permisos: 0

        return new AnalisisLimpieza(bytes, archivos, papelera.i64Size, papelera.i64NumItems);
    }

    public static ResultadoLimpieza Limpiar()
    {
        long liberados = 0;
        int salteados = 0;

        foreach (var archivo in TemporalesViejos().ToList())
        {
            bool eraSoloLectura = false;
            try
            {
                long tamaño = archivo.Length;
                eraSoloLectura = archivo.IsReadOnly;
                if (eraSoloLectura) archivo.IsReadOnly = false;   // los de solo lectura no se borran si no
                archivo.Delete();
                liberados += tamaño;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                salteados++;   // en uso o protegido: queda como estaba
                try { if (eraSoloLectura) archivo.IsReadOnly = true; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        BorrarCarpetasVacias();

        var papelera = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        if (SHQueryRecycleBin(null, ref papelera) == 0 && papelera.i64NumItems > 0 &&
            SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND) == 0)
        {
            liberados += papelera.i64Size;
        }

        Log.Escribir($"limpieza: liberados {liberados / 1048576} MB, {salteados} archivos salteados (en uso)");
        return new ResultadoLimpieza(liberados, salteados);
    }

    private static IEnumerable<FileInfo> TemporalesViejos()
    {
        var limite = DateTime.UtcNow - Antiguedad;
        if (CarpetaTemporal is not { } carpeta) yield break;
        var raiz = new DirectoryInfo(carpeta);
        if (!raiz.Exists) yield break;

        foreach (var archivo in raiz.EnumerateFiles("*", Recorrido))
        {
            bool viejo;
            try { viejo = archivo.LastWriteTimeUtc < limite; }
            catch (IOException) { continue; }
            if (viejo) yield return archivo;
        }
    }

    /// <summary>Carpetas que quedaron vacias y no son recientes, de la mas profunda hacia arriba.</summary>
    private static void BorrarCarpetasVacias()
    {
        var limite = DateTime.UtcNow - Antiguedad;
        if (CarpetaTemporal is not { } temporal) return;
        var raiz = new DirectoryInfo(temporal);
        if (!raiz.Exists) return;
        var carpetas = raiz.EnumerateDirectories("*", Recorrido).OrderByDescending(d => d.FullName.Length).ToList();
        foreach (var carpeta in carpetas)
        {
            try
            {
                if (carpeta.LastWriteTimeUtc < limite && !carpeta.EnumerateFileSystemInfos().Any())
                    carpeta.Delete(recursive: false);   // nunca recursivo: solo si esta vacia
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>"2,3 GB", "512 MB", "40 KB".</summary>
    public static string Formatear(long bytes, IFormatProvider cultura) => bytes switch
    {
        >= 1L << 30 => string.Format(cultura, "{0:0.0} GB", bytes / (double)(1L << 30)),
        >= 1L << 20 => string.Format(cultura, "{0:0} MB", bytes / (double)(1L << 20)),
        >= 1L << 10 => string.Format(cultura, "{0:0} KB", bytes / (double)(1L << 10)),
        _ => $"{bytes} B",
    };
}
