using System.Windows.Media;
using System.Windows.Threading;
using Windows.Devices.Enumeration.Pnp;
using Windows.Gaming.Input;

namespace DeskIsleBar.Modulos;

public enum NivelBateria { Ninguno, Bajo, Critico }

/// <summary>
/// Avisa la bateria baja de controles y dispositivos inalambricos, de la marca que sean. Tres fuentes:
/// <list type="bullet">
/// <item>XInput: controles de Xbox (4 niveles, los mismos que muestra Xbox Accessories).</item>
/// <item>Windows.Gaming.Input: cualquier otro control que informe su bateria (8BitDo, etc.).</item>
/// <item>La bateria Bluetooth que Windows muestra en Configuracion (auriculares y controles por Bluetooth).</item>
/// </list>
/// Un aviso al bajar del 20 % y otro (fijo) al bajar del 10 %; se rearma al cargar o si sube.
/// Hay controles que no informan bateria por ninguna via: de esos no se puede avisar.
/// </summary>
public class Baterias
{
    public const int UmbralBajo = 20, UmbralCritico = 10;
    public const string IconoControl = "", IconoBluetooth = "";

    // DEVPKEY_Bluetooth_Battery: la que usa Configuracion → Bluetooth para mostrar el porcentaje
    private const string PropiedadBateriaBluetooth = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string PropiedadNombre = "System.ItemNameDisplay";
    // Los de Xbox ya los cubre XInput, que es mas confiable con el adaptador inalambrico
    public const ushort VendorMicrosoft = 0x045E;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(5) };
    private readonly Dictionary<string, NivelBateria> _avisado = new();
    private bool _revisando;

    public event Action<Aviso>? Aviso;

    public void Iniciar()
    {
        _timer.Tick += async (_, _) => await RevisarAsync();
        _timer.Start();
        _ = RevisarAsync();
    }

    private record Lectura(string Id, string Nombre, string Icono, int? Porcentaje, NivelBateria Nivel);

    private async Task RevisarAsync()
    {
        if (_revisando) return;
        _revisando = true;
        try
        {
            var lecturas = new List<Lectura>();
            Juntar(lecturas, "xinput", LeerXInput);
            Juntar(lecturas, "controles", LeerControles);
            try { lecturas.AddRange(await LeerBluetoothAsync()); }
            catch (Exception ex) { Log.Escribir($"baterias: bluetooth: {ex.Message}"); }

            foreach (var lectura in lecturas) Evaluar(lectura);
        }
        finally
        {
            _revisando = false;
        }
    }

    /// <summary>Si una fuente falla (driver raro, API no disponible), las demas siguen.</summary>
    private static void Juntar(List<Lectura> lecturas, string fuente, Func<IEnumerable<Lectura>> leer)
    {
        try { lecturas.AddRange(leer()); }
        catch (Exception ex) { Log.Escribir($"baterias: {fuente}: {ex.Message}"); }
    }

    private static IEnumerable<Lectura> LeerXInput()
    {
        if (XInput.LeerPrimerControl() is not { } info || info.Tipo == XInput.TIPO_DESCONOCIDO) yield break;
        var nivel = info.Tipo == XInput.TIPO_CABLE || info.Nivel >= XInput.NIVEL_MEDIO ? NivelBateria.Ninguno
                  : info.Nivel == XInput.NIVEL_VACIO ? NivelBateria.Critico
                  : NivelBateria.Bajo;
        yield return new Lectura("xinput", Idioma.T("Bat.ControlXbox"), IconoControl, Porcentaje: null, nivel);
    }

    private static IEnumerable<Lectura> LeerControles()
    {
        foreach (var control in RawGameController.RawGameControllers)
        {
            if (control.HardwareVendorId == VendorMicrosoft) continue;
            var informe = control.TryGetBatteryReport();
            if (informe?.RemainingCapacityInMilliwattHours is not int resto ||
                informe.FullChargeCapacityInMilliwattHours is not int total || total <= 0) continue;

            int porcentaje = (int)Math.Round(100.0 * resto / total);
            bool cargando = informe.Status == Windows.System.Power.BatteryStatus.Charging;
            yield return new Lectura($"control:{control.NonRoamableId}", control.DisplayName, IconoControl, porcentaje,
                                     cargando ? NivelBateria.Ninguno : NivelDe(porcentaje));
        }
    }

    private static async Task<IEnumerable<Lectura>> LeerBluetoothAsync()
    {
        var dispositivos = await PnpObject.FindAllAsync(PnpObjectType.Device, [PropiedadBateriaBluetooth, PropiedadNombre]);
        return dispositivos
            .Where(d => d.Properties.TryGetValue(PropiedadBateriaBluetooth, out var v) && v is byte)
            .Select(d =>
            {
                int porcentaje = (byte)d.Properties[PropiedadBateriaBluetooth];
                string nombre = d.Properties.TryGetValue(PropiedadNombre, out var n) && n is string s ? s : "Bluetooth";
                return new Lectura($"bt:{d.Id}", nombre, IconoBluetooth, porcentaje, NivelDe(porcentaje));
            })
            .ToList();
    }

    private static NivelBateria NivelDe(int porcentaje) =>
        porcentaje <= UmbralCritico ? NivelBateria.Critico : porcentaje <= UmbralBajo ? NivelBateria.Bajo : NivelBateria.Ninguno;

    private void Evaluar(Lectura lectura)
    {
        var avisado = _avisado.GetValueOrDefault(lectura.Id);
        if (lectura.Nivel == NivelBateria.Ninguno)
        {
            if (avisado != NivelBateria.Ninguno) Log.Escribir($"baterias: rearmado {lectura.Nombre}");
            _avisado[lectura.Id] = NivelBateria.Ninguno;
            return;
        }
        if (lectura.Nivel <= avisado) return;   // ya avisado (o bajo despues de critico)

        _avisado[lectura.Id] = lectura.Nivel;
        Log.Escribir($"baterias: aviso {lectura.Nivel} {lectura.Nombre} ({lectura.Porcentaje?.ToString() ?? "?"}%)");
        Aviso?.Invoke(AvisoDe(lectura.Nombre, lectura.Nivel, lectura.Porcentaje, lectura.Icono));
    }

    public static Aviso AvisoDe(string nombre, NivelBateria nivel, int? porcentaje, string icono)
    {
        bool critico = nivel == NivelBateria.Critico;
        string cuanto = porcentaje is { } p ? $"{p}%" : critico ? $"~{UmbralCritico}%" : $"~{UmbralBajo}%";
        double carga = (porcentaje ?? (critico ? UmbralCritico : 25)) / 100.0;
        return critico
            ? new Aviso(icono, Idioma.T("Bat.CriticaTitulo", nombre), Idioma.T("Bat.CriticaDetalle", cuanto),
                        Color.FromRgb(0xFF, 0x45, 0x3A), carga, Persistente: true, Tipo: TiposAviso.Baterias)
            : new Aviso(icono, Idioma.T("Bat.BajaTitulo", nombre), Idioma.T("Bat.BajaDetalle", cuanto),
                        Color.FromRgb(0xFF, 0x9F, 0x0A), carga, Persistente: false, Tipo: TiposAviso.Baterias);
    }
}
