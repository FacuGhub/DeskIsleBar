using Microsoft.Win32;
using Windows.ApplicationModel;

namespace DeskIsleBar;

/// <summary>
/// Arrancar con Windows. Instalada con el instalador: entrada "DeskIsleBar" en HKCU\...\Run (solo el usuario actual,
/// sin admin). Empaquetada (Store): la tarea de inicio declarada en el manifiesto; el usuario tambien la ve y la
/// puede apagar desde Configuracion → Aplicaciones → Inicio.
/// </summary>
static class Arranque
{
    private const string ClaveRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Nombre = "DeskIsleBar";
    private const string TareaInicio = "DeskIsleBarArranque";   // TaskId del AppxManifest.xml

    public static bool Activo
    {
        get
        {
            if (Empaquetado.Activo)
                return TareaDeInicio()?.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            using var clave = Registry.CurrentUser.OpenSubKey(ClaveRun);
            return clave?.GetValue(Nombre) is string;
        }
    }

    /// <summary>Prende o apaga el arranque. Devuelve como quedo (empaquetada, Windows puede no dejar prenderlo).</summary>
    public static bool Fijar(bool activo)
    {
        try
        {
            if (Empaquetado.Activo) return FijarTarea(activo);
            using var clave = Registry.CurrentUser.CreateSubKey(ClaveRun);
            if (activo) clave.SetValue(Nombre, $"\"{Environment.ProcessPath}\"");
            else clave.DeleteValue(Nombre, throwOnMissingValue: false);
            Log.Escribir($"arranque: {(activo ? "activado" : "desactivado")}");
        }
        catch (Exception ex)
        {
            Log.Escribir($"arranque: no se pudo cambiar: {ex.GetType().Name}: {ex.Message}");
        }
        return Activo;
    }

    // Las llamadas de WinRT se esperan en otro hilo: bloquear el de la ventana esperandolas puede trabarla
    private static StartupTask? TareaDeInicio()
    {
        try { return Task.Run(async () => await StartupTask.GetAsync(TareaInicio)).GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            Log.Escribir($"arranque: no se encontro la tarea de inicio: {ex.GetType().Name}");
            return null;
        }
    }

    private static bool FijarTarea(bool activo)
    {
        if (TareaDeInicio() is not { } tarea) return false;
        if (!activo)
        {
            tarea.Disable();
            Log.Escribir("arranque: desactivado (tarea de inicio)");
            return false;
        }
        // Si el usuario la apago desde Configuracion o el Administrador de tareas, solo el la puede volver a prender
        var estado = Task.Run(async () => await tarea.RequestEnableAsync()).GetAwaiter().GetResult();
        Log.Escribir($"arranque: tarea de inicio {estado}");
        return estado is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }
}
