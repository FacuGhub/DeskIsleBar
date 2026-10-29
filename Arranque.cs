using Microsoft.Win32;

namespace DeskIsleBar;

/// <summary>Arrancar con Windows: entrada "DeskIsleBar" en HKCU\...\Run (solo el usuario actual, sin admin).</summary>
static class Arranque
{
    private const string ClaveRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Nombre = "DeskIsleBar";

    public static bool Activo
    {
        get
        {
            using var clave = Registry.CurrentUser.OpenSubKey(ClaveRun);
            return clave?.GetValue(Nombre) is string;
        }
    }

    public static void Fijar(bool activo)
    {
        try
        {
            using var clave = Registry.CurrentUser.CreateSubKey(ClaveRun);
            if (activo) clave.SetValue(Nombre, $"\"{Environment.ProcessPath}\"");
            else clave.DeleteValue(Nombre, throwOnMissingValue: false);
            Log.Escribir($"arranque: {(activo ? "activado" : "desactivado")}");
        }
        catch (Exception ex)
        {
            Log.Escribir($"arranque: no se pudo cambiar: {ex.Message}");
        }
    }
}
