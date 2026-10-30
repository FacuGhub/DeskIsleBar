using System.IO;

namespace DeskIsleBar;

/// <summary>Texto del panel de notas rapidas, en %LOCALAPPDATA%\DeskIsleBar\notas.txt.</summary>
static class Notas
{
    private static readonly string Archivo = Rutas.Archivo("notas.txt");

    public static string Leer()
    {
        try { return File.ReadAllText(Archivo); }
        catch (IOException) { return ""; }
    }

    public static void Guardar(string texto)
    {
        if (Demo.Activo) return;   // la nota de ejemplo no pisa la real
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Archivo)!);
            // Escritura atomica: si se corta a la mitad, la nota anterior queda intacta
            string temporal = Archivo + ".tmp";
            File.WriteAllText(temporal, texto);
            File.Move(temporal, Archivo, overwrite: true);
        }
        catch (IOException ex)
        {
            Log.Escribir($"notas: no se pudieron guardar: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
