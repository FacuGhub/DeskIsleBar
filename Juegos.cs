using Microsoft.Win32;

namespace DeskIsleBar;

/// <summary>
/// Los juegos que Windows ya reconoce (los que detecta la Xbox Game Bar: Steam, Battle.net, emuladores...).
/// Sirve para distinguir un juego en ventana sin bordes de un video a pantalla completa en el navegador.
/// </summary>
static class Juegos
{
    private const string Clave = @"System\GameConfigStore\Children";
    private static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(5);   // para tomar los juegos recien instalados

    private static HashSet<string> _rutas = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _leida = DateTime.MinValue;

    /// <summary>Cuantos juegos reconoce Windows (para el diagnostico).</summary>
    public static int Cantidad
    {
        get
        {
            if (DateTime.Now - _leida > Vigencia) Recargar();
            return _rutas.Count;
        }
    }

    public static bool EsJuego(string? rutaExe)
    {
        if (string.IsNullOrEmpty(rutaExe)) return false;
        if (DateTime.Now - _leida > Vigencia) Recargar();
        return _rutas.Contains(rutaExe);
    }

    private static void Recargar()
    {
        _leida = DateTime.Now;
        var rutas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var hijos = Registry.CurrentUser.OpenSubKey(Clave);
            foreach (string nombre in hijos?.GetSubKeyNames() ?? [])
            {
                using var juego = hijos!.OpenSubKey(nombre);
                if (juego?.GetValue("MatchedExeFullPath") is string ruta && ruta.Length > 0) rutas.Add(ruta);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            Log.Escribir($"juegos: no se pudo leer la lista de Windows: {ex.Message}");
            return;   // queda la lista anterior
        }
        _rutas = rutas;
    }
}
