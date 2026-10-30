using System.Globalization;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeskIsleBar.Modulos;

/// <summary>Servicio del que viene un evento, para abrir ese dia en su web (sin exponer el link privado).</summary>
public enum Servicio { Otro, Google, Outlook }

public record Evento(DateTime Inicio, bool TodoElDia, string Titulo, Color Color, Servicio Servicio);

/// <summary>Un calendario a leer. La URL llega ya descifrada y no sale de este modulo (ni al log).</summary>
public record FuenteCalendario(string Nombre, string Url, Color Color);

/// <summary>
/// Proximos eventos de uno o varios links privados .ics (Google Calendar: "Direccion secreta en formato iCal";
/// Outlook.com: "Publicar calendario"), mezclados y ordenados. Se renueva cada 15 minutos.
/// Repeticiones: entiende FREQ diaria/semanal (con BYDAY)/mensual/anual con INTERVAL, COUNT y UNTIL;
/// reglas mas raras (ej. "el segundo martes") muestran solo la primera fecha.
/// </summary>
public class Calendario
{
    // Tope de tamaño: un link que devuelva algo enorme no se come la memoria (un .ics normal pesa KB)
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 20 * 1024 * 1024 };
    private static readonly TimeSpan VentanaProximos = TimeSpan.FromDays(14);
    private const int MaxProximos = 4;
    // Rango que se carga para poder navegar el mini calendario: 2 meses atras y un año adelante
    private const int MesesAtras = 2, MesesAdelante = 12;

    private readonly DispatcherTimer _renovacion = new() { Interval = TimeSpan.FromMinutes(15) };
    private IReadOnlyList<FuenteCalendario> _fuentes = [];

    public IReadOnlyList<Evento> Proximos { get; private set; } = [];
    /// <summary>Todos los eventos del rango cargado, ordenados.</summary>
    public IReadOnlyList<Evento> Eventos { get; private set; } = [];
    public static DateTime DesdeCargado => new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-MesesAtras);
    public static DateTime HastaCargado => new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(MesesAdelante + 1);
    /// <summary>Nombres de los calendarios que no se pudieron leer en la ultima renovacion.</summary>
    public IReadOnlyList<string> ConError { get; private set; } = [];
    public event Action? Cambio;

    // ---------- Recordatorios ----------

    /// <summary>Cuanto antes de un evento se avisa (los de todo el dia no se avisan).</summary>
    public static readonly TimeSpan Anticipacion = TimeSpan.FromMinutes(10);
    private readonly DispatcherTimer _recordatorios = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly HashSet<(DateTime, string)> _recordados = [];

    /// <summary>Un evento empieza dentro de <see cref="Anticipacion"/> (una vez por evento).</summary>
    public event Action<Evento>? Recordatorio;

    public Calendario()
    {
        _renovacion.Tick += async (_, _) => await ActualizarAsync();
        _recordatorios.Tick += (_, _) => RevisarRecordatorios();
        _recordatorios.Start();
    }

    private void RevisarRecordatorios()
    {
        var ahora = DateTime.Now;
        foreach (var e in Eventos)
        {
            if (e.TodoElDia || e.Inicio <= ahora || e.Inicio - ahora > Anticipacion) continue;
            if (_recordados.Add((e.Inicio, e.Titulo))) Recordatorio?.Invoke(e);
        }
        _recordados.RemoveWhere(r => r.Item1 < ahora.AddHours(-1));   // que no crezca para siempre
    }

    public IReadOnlyList<FuenteCalendario> Fuentes
    {
        get => _fuentes;
        set
        {
            _fuentes = value;
            _renovacion.Stop();
            _renovacion.Start();
            _ = ActualizarAsync();
        }
    }

    public async Task ActualizarAsync()
    {
        var fuentes = _fuentes;
        if (fuentes.Count == 0)
        {
            Publicar([], []);
            return;
        }

        // Todos en paralelo; si uno falla, los demas se muestran igual
        var resultados = await Task.WhenAll(fuentes.Select(async fuente =>
        {
            try
            {
                string url = fuente.Url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase) ? "https://" + fuente.Url[9..] : fuente.Url;
                var servicio = url.Contains("google.com", StringComparison.OrdinalIgnoreCase) ? Servicio.Google
                             : url.Contains("outlook.", StringComparison.OrdinalIgnoreCase) || url.Contains("live.com", StringComparison.OrdinalIgnoreCase) ? Servicio.Outlook
                             : Servicio.Otro;
                return (Eventos: Parsear(await Http.GetStringAsync(url), fuente.Color, servicio), Error: (string?)null);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
            {
                // Solo el nombre: la URL es privada y no va al log
                Log.Escribir($"calendario «{fuente.Nombre}»: {ex.GetType().Name}");
                return (Eventos: new List<Evento>(), Error: fuente.Nombre);
            }
        }));
        if (!ReferenceEquals(fuentes, _fuentes)) return;   // se cambio la lista mientras se leia

        Publicar(resultados.SelectMany(r => r.Eventos), resultados.Select(r => r.Error).OfType<string>().ToList());
    }

    /// <summary>Modo demo (capturas): eventos inventados en lugar de los links; no se renueva ni se descarga nada.</summary>
    public void MostrarDemo(IEnumerable<Evento> eventos)
    {
        _fuentes = [];
        _renovacion.Stop();
        Publicar(eventos, []);
    }

    private void Publicar(IEnumerable<Evento> todos, IReadOnlyList<string> conError)
    {
        var desde = DesdeCargado;
        var eventos = todos.Where(e => e.Inicio >= desde).OrderBy(e => e.Inicio).ToList();
        var hoy = DateTime.Today;
        Proximos = eventos.Where(e => e.Inicio >= (e.TodoElDia ? hoy : DateTime.Now.AddMinutes(-30)) && e.Inicio < hoy + VentanaProximos)
                          .Take(MaxProximos).ToList();
        Eventos = eventos;
        ConError = conError;
        Cambio?.Invoke();
    }

    public IEnumerable<Evento> DelDia(DateOnly dia) => Eventos.Where(e => DateOnly.FromDateTime(e.Inicio) == dia);

    /// <summary>Por cada dia del mes con eventos, los colores distintos de sus calendarios (para los puntitos).</summary>
    public Dictionary<DateOnly, List<Color>> ColoresDelMes(int año, int mes)
    {
        var dias = new Dictionary<DateOnly, List<Color>>();
        foreach (var e in Eventos.Where(e => e.Inicio.Year == año && e.Inicio.Month == mes))
        {
            var dia = DateOnly.FromDateTime(e.Inicio);
            if (!dias.TryGetValue(dia, out var colores)) dias[dia] = colores = [];
            if (!colores.Contains(e.Color)) colores.Add(e.Color);
        }
        return dias;
    }

    // ---------- Lectura del .ics ----------

    private static List<Evento> Parsear(string ics, Color color, Servicio servicio)
    {
        // Las lineas largas siguen en la proxima, empezando con espacio o tab ("unfolding")
        var lineas = new List<string>();
        foreach (var cruda in ics.Replace("\r\n", "\n").Split('\n'))
        {
            if ((cruda.StartsWith(' ') || cruda.StartsWith('\t')) && lineas.Count > 0) lineas[^1] += cruda[1..];
            else lineas.Add(cruda);
        }

        var eventos = new List<Evento>();
        var limite = HastaCargado;
        Dictionary<string, (string Parametros, string Valor)>? actual = null;

        foreach (var linea in lineas)
        {
            if (linea == "BEGIN:VEVENT") { actual = new(); continue; }
            if (linea == "END:VEVENT")
            {
                if (actual is not null) AgregarEvento(actual, eventos, limite, color, servicio);
                actual = null;
                continue;
            }
            if (actual is null) continue;

            int dosPuntos = linea.IndexOf(':');
            if (dosPuntos < 0) continue;
            string nombreYParametros = linea[..dosPuntos];
            int puntoYComa = nombreYParametros.IndexOf(';');
            string nombre = puntoYComa < 0 ? nombreYParametros : nombreYParametros[..puntoYComa];
            string parametros = puntoYComa < 0 ? "" : nombreYParametros[(puntoYComa + 1)..];
            actual.TryAdd(nombre, (parametros, linea[(dosPuntos + 1)..]));
        }
        return eventos;
    }

    private static void AgregarEvento(Dictionary<string, (string Parametros, string Valor)> e, List<Evento> eventos, DateTime limite,
                                      Color color, Servicio servicio)
    {
        if (!e.TryGetValue("DTSTART", out var inicioCrudo)) return;
        if (e.TryGetValue("STATUS", out var estado) && estado.Valor == "CANCELLED") return;
        if (LeerFecha(inicioCrudo.Parametros, inicioCrudo.Valor, out bool todoElDia) is not { } inicio) return;

        string titulo = e.TryGetValue("SUMMARY", out var resumen)
            ? resumen.Valor.Replace("\\,", ",").Replace("\\;", ";").Replace("\\n", " ").Replace("\\\\", "\\")
            : Idioma.T("Cal.SinTitulo");

        if (!e.TryGetValue("RRULE", out var regla))
        {
            eventos.Add(new Evento(inicio, todoElDia, titulo, color, servicio));
            return;
        }

        foreach (var fecha in Repeticiones(inicio, regla.Valor, limite))
            eventos.Add(new Evento(fecha, todoElDia, titulo, color, servicio));
    }

    private static IEnumerable<DateTime> Repeticiones(DateTime inicio, string regla, DateTime limite)
    {
        var partes = regla.Split(';').Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
                          .ToDictionary(p => p[0], p => p[1]);
        string frecuencia = partes.GetValueOrDefault("FREQ", "");
        int intervalo = int.TryParse(partes.GetValueOrDefault("INTERVAL"), out int i) && i > 0 ? i : 1;
        int maximo = int.TryParse(partes.GetValueOrDefault("COUNT"), out int c) ? c : int.MaxValue;
        DateTime hasta = partes.TryGetValue("UNTIL", out var u) && LeerFecha("", u, out _) is { } fin && fin < limite ? fin : limite;

        if (frecuencia == "WEEKLY" && partes.TryGetValue("BYDAY", out var dias))
        {
            var diasSemana = dias.Split(',').Select(DiaDeSemana).OfType<DayOfWeek>().ToHashSet();
            var lunes = inicio.Date.AddDays(-(((int)inicio.DayOfWeek + 6) % 7));
            int emitidos = 0;
            for (var semana = lunes; semana <= hasta && emitidos < maximo; semana = semana.AddDays(7 * intervalo))
            {
                for (int d = 0; d < 7 && emitidos < maximo; d++)
                {
                    var fecha = semana.AddDays(d) + inicio.TimeOfDay;
                    if (fecha < inicio || fecha > hasta || !diasSemana.Contains(fecha.DayOfWeek)) continue;
                    emitidos++;
                    yield return fecha;
                }
            }
            yield break;
        }

        Func<DateTime, DateTime>? siguiente = frecuencia switch
        {
            "DAILY" => f => f.AddDays(intervalo),
            "WEEKLY" => f => f.AddDays(7 * intervalo),
            "MONTHLY" when !partes.ContainsKey("BYDAY") => f => f.AddMonths(intervalo),
            "YEARLY" when !partes.ContainsKey("BYDAY") => f => f.AddYears(intervalo),
            _ => null,   // regla no soportada: solo la primera fecha
        };
        if (siguiente is null)
        {
            yield return inicio;
            yield break;
        }

        int n = 0;
        for (var fecha = inicio; fecha <= hasta && n < maximo && n < 20000; fecha = siguiente(fecha), n++)
            yield return fecha;
    }

    private static DayOfWeek? DiaDeSemana(string codigo) => codigo[^2..] switch
    {
        "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday, "TH" => DayOfWeek.Thursday,
        "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday, "SU" => DayOfWeek.Sunday, _ => null,
    };

    /// <summary>
    /// "20261027" (todo el dia), "20261027T150000Z" (UTC), "20261027T150000" con TZID (zona del evento)
    /// o sin TZID (hora local). Devuelve hora local.
    /// </summary>
    private static DateTime? LeerFecha(string parametros, string valor, out bool todoElDia)
    {
        todoElDia = false;
        var cultura = CultureInfo.InvariantCulture;

        if (valor.Length == 8 && DateTime.TryParseExact(valor, "yyyyMMdd", cultura, DateTimeStyles.None, out var dia))
        {
            todoElDia = true;
            return dia;
        }
        if (valor.EndsWith('Z') && DateTime.TryParseExact(valor, "yyyyMMdd'T'HHmmss'Z'", cultura,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc))
            return utc.ToLocalTime();
        if (!DateTime.TryParseExact(valor, "yyyyMMdd'T'HHmmss", cultura, DateTimeStyles.None, out var local))
            return null;

        string? zona = parametros.Split(';').FirstOrDefault(p => p.StartsWith("TZID="))?[5..].Trim('"');
        if (zona is null) return local;
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(zona);   // .NET acepta nombres IANA ("America/Argentina/Buenos_Aires")
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), tz).ToLocalTime();
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return local;
        }
    }
}
