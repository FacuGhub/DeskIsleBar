using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DeskIsleBar.Modulos;

/// <summary>
/// Avisa cada vez que cambia el volumen (o el mute) de la salida de audio predeterminada.
/// Si cambia la salida predeterminada (parlantes -> auriculares), se engancha a la nueva.
/// </summary>
public class Volumen : IMMNotificationClient
{
    private readonly Dispatcher _ui;
    private readonly MMDeviceEnumerator _enumerador = new();
    private MMDevice? _dispositivo;

    /// <summary>nivel 0..1, silenciado</summary>
    public event Action<float, bool>? Cambio;

    public Volumen(Dispatcher ui) => _ui = ui;

    public void Iniciar()
    {
        _enumerador.RegisterEndpointNotificationCallback(this);
        Conectar();
    }

    private void Conectar()
    {
        if (_dispositivo is not null)
        {
            _dispositivo.AudioEndpointVolume.OnVolumeNotification -= AlCambiar;
            _dispositivo.Dispose();
            _dispositivo = null;
        }

        try
        {
            _dispositivo = _enumerador.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _dispositivo.AudioEndpointVolume.OnVolumeNotification += AlCambiar;
        }
        catch (Exception ex)
        {
            Log.Escribir($"volumen: no hay salida de audio: {ex.Message}");
        }
    }

    /// <summary>Sube o baja (rueda del mouse). Subir saca el mute.</summary>
    public void Ajustar(float delta)
    {
        if (_dispositivo?.AudioEndpointVolume is not { } volumen) return;
        Fijar(volumen.MasterVolumeLevelScalar + delta);
    }

    /// <summary>Lleva el volumen a un nivel 0..1, redondeado a pasos de 2 %.</summary>
    public void Fijar(float nivel)
    {
        if (_dispositivo?.AudioEndpointVolume is not { } volumen) return;
        float nuevo = Math.Clamp(MathF.Round(nivel * 50) / 50, 0f, 1f);
        try
        {
            if (volumen.Mute && nuevo > volumen.MasterVolumeLevelScalar) volumen.Mute = false;
            volumen.MasterVolumeLevelScalar = nuevo;
        }
        catch (Exception ex)
        {
            Log.Escribir($"volumen: no se pudo cambiar: {ex.Message}");
        }
    }

    // ---------- Volumen de la app de musica ----------

    /// <summary>
    /// Apps que suenan en modo exclusivo: saltean el mezclador (su volumen ahi no hace nada), asi que se
    /// mueve el de la salida por la que suenan.
    /// </summary>
    private static readonly string[] ModoExclusivo = ["TIDAL"];

    /// <summary>
    /// Sube o baja el volumen propio de la app de musica (el del mezclador de Windows, no el general).
    /// Devuelve el nivel resultante, o null si esa app no tiene sesion de audio abierta.
    /// </summary>
    public (float Nivel, bool Silenciado)? AjustarApp(string appMusica, float delta) =>
        CambiarApp(appMusica, actual => actual + delta);

    public (float Nivel, bool Silenciado)? FijarApp(string appMusica, float nivel) =>
        CambiarApp(appMusica, _ => nivel);

    private (float Nivel, bool Silenciado)? CambiarApp(string appMusica, Func<float, float> calcular)
    {
        string proceso = Musica.ProcesoDeApp(appMusica);
        var sesiones = SesionesDe(proceso);
        if (sesiones.Count == 0) return null;

        static float Redondear(float nivel) => Math.Clamp(MathF.Round(nivel * 50) / 50, 0f, 1f);
        try
        {
            if (ModoExclusivo.Any(p => proceso.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                var volumen = sesiones[0].Dispositivo.AudioEndpointVolume;
                float actual = volumen.MasterVolumeLevelScalar, nuevo = Redondear(calcular(actual));
                if (volumen.Mute && nuevo > actual) volumen.Mute = false;
                volumen.MasterVolumeLevelScalar = nuevo;
                return (nuevo, volumen.Mute);
            }

            // Todas las sesiones del proceso (Chrome puede tener una por salida) quedan al mismo nivel
            var principal = sesiones[0].Sesion.SimpleAudioVolume;
            float anterior = principal.Volume, nivel = Redondear(calcular(anterior));
            foreach (var (_, sesion) in sesiones)
            {
                var volumen = sesion.SimpleAudioVolume;
                if (volumen.Mute && nivel > anterior) volumen.Mute = false;
                volumen.Volume = nivel;
            }
            return (nivel, principal.Mute);
        }
        catch (Exception ex)
        {
            Log.Escribir($"volumen: no se pudo cambiar el de la app: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Sesiones de audio de la app de musica, la que esta sonando primero. SMTC identifica a la app por su
    /// AppUserModelId y el audio va por proceso: se cruzan por el nombre del proceso (por prefijo: Tidal suena
    /// desde "TIDALPlayer"). Se busca en TODAS las salidas: la app puede usar una distinta de la predeterminada.
    /// </summary>
    private List<(MMDevice Dispositivo, AudioSessionControl Sesion)> SesionesDe(string nombreProceso)
    {
        var encontradas = new List<(MMDevice, AudioSessionControl)>();
        foreach (var dispositivo in _enumerador.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            var gestor = dispositivo.AudioSessionManager;
            gestor.RefreshSessions();
            var sesiones = gestor.Sessions;
            for (int i = 0; i < sesiones.Count; i++)
            {
                var sesion = sesiones[i];
                if (sesion.IsSystemSoundsSession) continue;
                try
                {
                    using var proceso = System.Diagnostics.Process.GetProcessById((int)sesion.GetProcessID);
                    if (!proceso.ProcessName.StartsWith(nombreProceso, StringComparison.OrdinalIgnoreCase)) continue;
                }
                catch (ArgumentException) { continue; }   // el proceso ya termino

                if (sesion.State == AudioSessionState.AudioSessionStateActive) encontradas.Insert(0, (dispositivo, sesion));
                else encontradas.Add((dispositivo, sesion));
            }
        }
        return encontradas;
    }

    // Llega en un hilo de audio: se pasa al de la interfaz
    private void AlCambiar(AudioVolumeNotificationData datos) =>
        _ui.InvokeAsync(() => Cambio?.Invoke(datos.MasterVolume, datos.Muted));

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia) _ui.InvokeAsync(Conectar);
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
}
