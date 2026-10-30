using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Windows.Networking.Connectivity;

namespace DeskIsleBar.Modulos;

/// <summary>
/// Avisos del sistema: camara o microfono en uso (como los puntitos del celular), bateria de la notebook,
/// Bloq Mayus e internet caida / de vuelta. Cada uno lleva su <see cref="Aviso.Tipo"/> para poder apagarlo en Opciones.
/// </summary>
public class AvisosSistema
{
    public const string TipoCamara = "camara", TipoBateriaPc = "bateriapc", TipoBloqMayus = "bloqmayus", TipoInternet = "internet";

    private static readonly Color Verde = Color.FromRgb(0x30, 0xD1, 0x58);
    private static readonly Color Naranja = Color.FromRgb(0xFF, 0x9F, 0x0A);
    private static readonly Color Rojo = Color.FromRgb(0xFF, 0x45, 0x3A);
    private static readonly Color Gris = Color.FromRgb(0x98, 0x98, 0x9D);

    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _rapido = new() { Interval = TimeSpan.FromMilliseconds(250) };   // Bloq Mayus
    private readonly DispatcherTimer _lento = new() { Interval = TimeSpan.FromSeconds(2) };           // camara, bateria
    private readonly DispatcherTimer _confirmarRed = new() { Interval = TimeSpan.FromSeconds(4) };   // la red parpadea al cambiar

    public event Action<Aviso>? Aviso;

    /// <summary>
    /// Cambio lo que esta en uso (camara, microfono, Bloq Mayus). El argumento es lo que acaba de EMPEZAR
    /// ("webcam", "microphone", "bloqmayus") para el destello de la isla, o null si solo termino algo.
    /// </summary>
    public event Action<string?>? EnUsoCambio;

    /// <summary>Apps usando la camara / el microfono ahora (nombres para mostrar, sin repetir).</summary>
    public IReadOnlyList<string> AppsCamara => AppsDe("webcam");
    public IReadOnlyList<string> AppsMicrofono => AppsDe("microphone");
    public bool BloqMayus => _bloqMayus;

    private List<string> AppsDe(string recurso) =>
        _enUso.Where(u => u.Recurso == recurso).Select(u => u.App).Distinct(StringComparer.OrdinalIgnoreCase)
              .Order(StringComparer.OrdinalIgnoreCase).ToList();

    public AvisosSistema(Dispatcher ui) => _ui = ui;

    public void Iniciar()
    {
        SensoresCamara.Iniciar();

        // Estado inicial sin avisar: solo cuentan los cambios desde ahora
        _bloqMayus = BloqMayusActivo();
        _enUso = AppsUsando();
        _internet = HayInternet();
        LeerBateria(avisar: false);

        _rapido.Tick += (_, _) => RevisarBloqMayus();
        _lento.Tick += (_, _) =>
        {
            Seguro("camara", RevisarCamaraYMicrofono);
            Seguro("bateria", () => LeerBateria(avisar: true));
        };
        _confirmarRed.Tick += (_, _) => { _confirmarRed.Stop(); Seguro("internet", RevisarInternet); };
        NetworkInformation.NetworkStatusChanged += _ => _ui.InvokeAsync(() => { _confirmarRed.Stop(); _confirmarRed.Start(); });

        _rapido.Start();
        _lento.Start();
        EnUsoCambio?.Invoke(null);   // lo que ya estaba en uso al arrancar se ve, sin destello
    }

    private static void Seguro(string que, Action accion)
    {
        try { accion(); }
        catch (Exception ex) { Log.Escribir($"avisos: {que}: {ex.GetType().Name}: {ex.Message}"); }
    }

    private void Avisar(string icono, string titulo, string detalle, Color color, string tipo, double segundos = 4, bool fijo = false) =>
        Aviso?.Invoke(new Aviso(icono, titulo, detalle, color, Carga: null, Persistente: fijo, segundos, tipo));

    // ---------- Bloq Mayus ----------

    [DllImport("user32.dll")] private static extern short GetKeyState(int tecla);
    private const int VK_CAPITAL = 0x14;
    private bool _bloqMayus;

    private static bool BloqMayusActivo() => (GetKeyState(VK_CAPITAL) & 1) != 0;

    private void RevisarBloqMayus()
    {
        bool activo = BloqMayusActivo();
        if (activo == _bloqMayus) return;
        _bloqMayus = activo;
        EnUsoCambio?.Invoke(activo ? "bloqmayus" : null);
        Avisar("", Idioma.T(activo ? "Av.BloqMayusSi" : "Av.BloqMayusNo"), "", activo ? Naranja : Gris, TipoBloqMayus, segundos: 1.5);
    }

    // ---------- Camara y microfono ----------

    // Windows anota cada uso en el registro: mientras una app los usa, LastUsedTimeStop queda en 0
    private const string Consentimientos = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    private HashSet<(string Recurso, string App)> _enUso = [];

    private void RevisarCamaraYMicrofono()
    {
        var ahora = AppsUsando();
        bool cambio = !ahora.SetEquals(_enUso);
        var empezo = ahora.Except(_enUso).Select(u => u.Item1).ToHashSet();
        foreach (var (recurso, app) in ahora.Except(_enUso))
        {
            bool camara = recurso == "webcam";
            Avisar(camara ? "" : "", Idioma.T(camara ? "Av.CamaraSi" : "Av.MicrofonoSi"), app,
                   camara ? Verde : Naranja, TipoCamara);
        }
        foreach (var (recurso, app) in _enUso.Except(ahora))
        {
            bool camara = recurso == "webcam";
            Avisar(camara ? "" : "", Idioma.T(camara ? "Av.CamaraNo" : "Av.MicrofonoNo"), app, Gris, TipoCamara, segundos: 2.5);
        }
        _enUso = ahora;
        if (cambio) EnUsoCambio?.Invoke(empezo.Contains("webcam") ? "webcam" : empezo.Contains("microphone") ? "microphone" : null);
    }

    /// <summary>
    /// Registro de Windows (camara y microfono) + sesiones de grabacion activas en el audio (microfono). El registro
    /// solo lo llenan las apps que piden el permiso por la via oficial; el audio ve a cualquiera que este grabando.
    /// </summary>
    private static HashSet<(string, string)> AppsUsando()
    {
        var usando = AppsUsandoSegunRegistro();
        foreach (var app in AppsGrabandoAudio()) usando.Add(("microphone", app));
        foreach (var pid in SensoresCamara.Procesos)
            if (NombreDeProceso(pid) is { } app) usando.Add(("webcam", app));
        return usando;
    }

    private static string? NombreDeProceso(int pid)
    {
        try
        {
            using var proceso = System.Diagnostics.Process.GetProcessById(pid);
            string? ruta = null;
            try { ruta = proceso.MainModule?.FileName; } catch (System.ComponentModel.Win32Exception) { }   // proceso con mas permisos
            return ruta is null ? proceso.ProcessName : NombreDeEjecutable(ruta);
        }
        catch (ArgumentException) { return null; }   // el proceso ya termino
    }

    private static readonly NAudio.CoreAudioApi.MMDeviceEnumerator Audio = new();

    private static IEnumerable<string> AppsGrabandoAudio()
    {
        var apps = new HashSet<string>();
        foreach (var microfono in Audio.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active))
        {
            var gestor = microfono.AudioSessionManager;
            gestor.RefreshSessions();
            var sesiones = gestor.Sessions;
            for (int i = 0; i < sesiones.Count; i++)
            {
                var sesion = sesiones[i];
                if (sesion.IsSystemSoundsSession || sesion.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive) continue;
                if (NombreDeProceso((int)sesion.GetProcessID) is { } app) apps.Add(app);
            }
        }
        return apps;
    }

    private static HashSet<(string, string)> AppsUsandoSegunRegistro()
    {
        var usando = new HashSet<(string, string)>();
        foreach (var recurso in new[] { "webcam", "microphone" })
        {
            using var raiz = Registry.CurrentUser.OpenSubKey($@"{Consentimientos}\{recurso}");
            if (raiz is null) continue;
            foreach (var nombre in raiz.GetSubKeyNames())
            {
                using var clave = raiz.OpenSubKey(nombre);
                if (clave is null) continue;
                if (nombre == "NonPackaged")
                {
                    // Apps de escritorio: una subclave por ejecutable, con la ruta y "#" en lugar de "\"
                    foreach (var exe in clave.GetSubKeyNames())
                    {
                        using var sub = clave.OpenSubKey(exe);
                        string ruta = exe.Replace('#', '\\');
                        // Si la app se cerro de golpe queda "en uso" para siempre; las versiones viejas (ej. Discord
                        // app-1.0.9244, borrada al actualizarse) no existen mas en el disco: esas se ignoran
                        if (EnUso(sub) && File.Exists(ruta)) usando.Add((recurso, NombreDeEjecutable(ruta)));
                    }
                }
                else if (EnUso(clave))
                {
                    usando.Add((recurso, NombreDePaquete(nombre)));
                }
            }
        }
        return usando;
    }

    private static bool EnUso(RegistryKey? clave) =>
        clave?.GetValue("LastUsedTimeStart") is long inicio && inicio != 0 &&
        clave.GetValue("LastUsedTimeStop") is long fin && fin == 0;

    private static string NombreDeEjecutable(string ruta)
    {
        try
        {
            string? descripcion = System.Diagnostics.FileVersionInfo.GetVersionInfo(ruta).FileDescription;
            if (!string.IsNullOrWhiteSpace(descripcion)) return descripcion;
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or ArgumentException) { }
        return Path.GetFileNameWithoutExtension(ruta);
    }

    private static readonly Dictionary<string, string> NombresDePaquete = new();

    /// <summary>
    /// "Microsoft.WindowsCamera_8wekyb3d8bbwe" → "Cámara": el nombre que muestra Windows (casi todas las apps de la
    /// Store se llaman "App" adentro del paquete). Si no se resuelve, "WindowsCamera". Corre en el hilo de la ventana (STA).
    /// </summary>
    private static string NombreDePaquete(string familia)
    {
        if (NombresDePaquete.TryGetValue(familia, out var nombre)) return nombre;
        string sinEditor = familia.Split('_')[0];
        nombre = NotificacionesWindows.NombreDesdeShell($"{familia}!App") ?? sinEditor[(sinEditor.LastIndexOf('.') + 1)..];
        NombresDePaquete[familia] = nombre;
        return nombre;
    }

    // ---------- Bateria de la notebook ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct EstadoEnergia { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out EstadoEnergia estado);
    private const byte SIN_BATERIA = 128, DESCONOCIDO = 255;

    private bool? _enchufada;
    private NivelBateria _avisadoPc;

    private void LeerBateria(bool avisar)
    {
        if (!GetSystemPowerStatus(out var e) || (e.BatteryFlag & SIN_BATERIA) != 0 || e.BatteryFlag == DESCONOCIDO ||
            e.BatteryLifePercent == DESCONOCIDO || e.ACLineStatus == DESCONOCIDO) return;   // PC de escritorio: nada que avisar

        bool enchufada = e.ACLineStatus == 1;
        int porcentaje = e.BatteryLifePercent;
        if (avisar && _enchufada is { } antes && antes != enchufada)
        {
            Avisar("", Idioma.T(enchufada ? "Av.Enchufada" : "Av.Desenchufada"), $"{porcentaje}%",
                   enchufada ? Verde : Gris, TipoBateriaPc, segundos: 3);
        }
        _enchufada = enchufada;

        var nivel = enchufada ? NivelBateria.Ninguno
                  : porcentaje <= Baterias.UmbralCritico ? NivelBateria.Critico
                  : porcentaje <= Baterias.UmbralBajo ? NivelBateria.Bajo
                  : NivelBateria.Ninguno;
        if (nivel == NivelBateria.Ninguno) { _avisadoPc = NivelBateria.Ninguno; return; }
        if (!avisar || nivel <= _avisadoPc) return;

        _avisadoPc = nivel;
        bool critico = nivel == NivelBateria.Critico;
        Aviso?.Invoke(new Aviso("", Idioma.T(critico ? "Av.PcCritica" : "Av.PcBaja"), Idioma.T("Av.PcDetalle", porcentaje),
                                critico ? Rojo : Naranja, porcentaje / 100.0, Persistente: critico, Tipo: TipoBateriaPc));
    }

    // ---------- Internet ----------

    private bool _internet;

    private static bool HayInternet() =>
        NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;

    private void RevisarInternet()
    {
        bool hay = HayInternet();
        if (hay == _internet) return;   // fue un parpadeo: volvio al mismo estado antes de confirmar
        _internet = hay;
        string red = hay ? NetworkInformation.GetInternetConnectionProfile()?.ProfileName ?? "" : "";
        Avisar(hay ? "" : "", Idioma.T(hay ? "Av.InternetSi" : "Av.InternetNo"), red, hay ? Verde : Rojo, TipoInternet);
    }
}
