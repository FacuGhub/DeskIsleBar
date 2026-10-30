using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DeskIsleBar.Modulos;

/// <summary>
/// Avisa cuando se conecta o desconecta: el joystick (XInput), una salida de audio
/// (auriculares, DAC, parlantes) o un pendrive/disco USB.
/// </summary>
public class Dispositivos : IMMNotificationClient
{
    private static readonly Color Verde = Color.FromRgb(0x30, 0xD1, 0x58);
    private static readonly Color Gris = Color.FromRgb(0x98, 0x98, 0x9D);
    private const double Segundos = 3.5;

    private readonly Dispatcher _ui;
    private readonly MMDeviceEnumerator _enumerador = new();
    private readonly Dictionary<string, (string Nombre, bool Activo)> _audio = new();
    private readonly DispatcherTimer _sondeo = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _joystickConectado;
    private bool _anunciarJoystick;   // recien conectado: se anuncia en el proximo sondeo, cuando ya informa la bateria
    // Controles que no son de Xbox (PlayStation, 8BitDo, genericos), por Windows.Gaming.Input: id → nombre
    private readonly Dictionary<string, string> _controles = new();

    public event Action<Aviso>? Aviso;

    public Dispositivos(Dispatcher ui) => _ui = ui;

    public void Iniciar()
    {
        // Estado inicial sin avisar: solo cuentan los cambios desde ahora
        _joystickConectado = XInput.LeerPrimerControl() is not null;
        foreach (var (id, nombre) in ControlesConectados()) _controles[id] = nombre;
        foreach (var d in _enumerador.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All))
            _audio[d.ID] = (Nombre(d), d.State == DeviceState.Active);

        _enumerador.RegisterEndpointNotificationCallback(this);
        _sondeo.Tick += (_, _) => { SondearJoystick(); SondearControles(); };
        _sondeo.Start();
    }

    // ---------- Joystick ----------

    private void SondearJoystick()
    {
        var bateria = XInput.LeerPrimerControl();
        bool conectado = bateria is not null;

        if (_anunciarJoystick && conectado)
        {
            _anunciarJoystick = false;
            Avisar("", Idioma.T("Disp.JoystickConectado"), XInput.Describir(bateria!.Value), Verde);
        }

        if (conectado == _joystickConectado) return;
        _joystickConectado = conectado;

        if (conectado) _anunciarJoystick = true;
        else
        {
            _anunciarJoystick = false;
            Avisar("", Idioma.T("Disp.JoystickDesconectado"), "", Gris);
        }
    }

    private void SondearControles()
    {
        Dictionary<string, string> ahora;
        try { ahora = ControlesConectados().ToDictionary(c => c.Id, c => c.Nombre); }
        catch (Exception ex)
        {
            Log.Escribir($"dispositivos: controles: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        foreach (var (id, nombre) in ahora.Where(c => !_controles.ContainsKey(c.Key)))
            Avisar(Baterias.IconoControl, Idioma.T("Disp.JoystickConectado"), nombre, Verde);
        foreach (var (id, nombre) in _controles.Where(c => !ahora.ContainsKey(c.Key)))
            Avisar(Baterias.IconoControl, Idioma.T("Disp.JoystickDesconectado"), nombre, Gris);

        _controles.Clear();
        foreach (var (id, nombre) in ahora) _controles[id] = nombre;
    }

    /// <summary>Controles que ve Windows y que no son de Xbox (esos los cubre XInput, con su bateria).</summary>
    private static IEnumerable<(string Id, string Nombre)> ControlesConectados() =>
        Windows.Gaming.Input.RawGameController.RawGameControllers
            .Where(c => c.HardwareVendorId != Baterias.VendorMicrosoft)
            .Select(c => (c.NonRoamableId, string.IsNullOrWhiteSpace(c.DisplayName) ? Idioma.T("Disp.ControlGenerico") : c.DisplayName));

    // ---------- Audio ----------

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => _ui.InvokeAsync(() =>
    {
        bool activo = newState == DeviceState.Active;
        string? nombre = null;
        if (_audio.TryGetValue(deviceId, out var previo))
        {
            if (previo.Activo == activo) return;   // el mismo cambio puede llegar repetido
            nombre = previo.Nombre;
        }

        try
        {
            using var dispositivo = _enumerador.GetDevice(deviceId);
            if (dispositivo.DataFlow != DataFlow.Render) return;   // solo salidas (si no, un headset avisa dos veces)
            nombre = Nombre(dispositivo);
        }
        catch (COMException) { }   // ya no esta: se usa el nombre guardado

        if (nombre is null) return;
        _audio[deviceId] = (nombre, activo);
        Avisar(activo ? "" : "", Idioma.T(activo ? "Disp.AudioConectado" : "Disp.AudioDesconectado"), nombre, activo ? Verde : Gris);
    });

    private static string Nombre(MMDevice d)
    {
        try { return d.FriendlyName; } catch (COMException) { return Idioma.T("Disp.DispositivoAudio"); }
    }

    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    // ---------- USB (pendrives / discos) ----------

    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVTYP_VOLUME = 2;
    private const short DBTF_MEDIA = 1, DBTF_NET = 2;

    /// <summary>Lo llama la ventana con cada mensaje: Windows avisa los volumenes nuevos a las ventanas principales.</summary>
    public void ProcesarMensaje(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != WM_DEVICECHANGE || lParam == IntPtr.Zero) return;
        int evento = wParam.ToInt32();
        if (evento != DBT_DEVICEARRIVAL && evento != DBT_DEVICEREMOVECOMPLETE) return;

        // DEV_BROADCAST_VOLUME: size, devicetype, reserved, unitmask, flags
        if (Marshal.ReadInt32(lParam, 4) != DBT_DEVTYP_VOLUME) return;
        short flags = Marshal.ReadInt16(lParam, 16);
        if ((flags & (DBTF_MEDIA | DBTF_NET)) != 0) return;   // cambio de disco en lectora / unidad de red

        int mascara = Marshal.ReadInt32(lParam, 12);
        for (int i = 0; i < 26; i++)
        {
            if ((mascara & (1 << i)) == 0) continue;
            string letra = $"{(char)('A' + i)}:";

            if (evento == DBT_DEVICEARRIVAL)
            {
                string etiqueta = "";
                try { etiqueta = new DriveInfo(letra).VolumeLabel; } catch (IOException) { }
                Avisar("", Idioma.T("Disp.UsbConectado"), string.IsNullOrEmpty(etiqueta) ? letra : $"{etiqueta} ({letra})", Verde);
            }
            else
            {
                Avisar("", Idioma.T("Disp.UsbDesconectado"), letra, Gris);
            }
        }
    }

    private void Avisar(string icono, string titulo, string detalle, Color color) =>
        Aviso?.Invoke(new Aviso(icono, titulo, detalle, color, Carga: null, Persistente: false, Segundos, TiposAviso.Dispositivos));
}
