using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;

namespace DeskIsleBar.Modulos;

public record EstadoClima(string Lugar, double Temperatura, double Sensacion, int Codigo, bool EsDeDia,
                          int Humedad, double Viento, double Maxima, double Minima);

/// <summary>
/// Clima actual por Open-Meteo (gratis, sin cuenta ni clave). La ciudad se elige en Opciones
/// y se convierte a coordenadas con su geocodificador. Se renueva cada 15 minutos.
/// </summary>
public class Clima
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 1024 * 1024 };

    private readonly DispatcherTimer _renovacion = new() { Interval = TimeSpan.FromMinutes(15) };
    private string _ciudad = "";
    private (double Latitud, double Longitud, string Nombre)? _lugar;
    private string? _lugarDe;   // ciudad para la que se geocodifico _lugar

    public EstadoClima? Estado { get; private set; }
    // Se guarda la clave y no el texto: si cambia el idioma, el mensaje sale en el nuevo
    private (string Clave, string Valor)? _problema = ("Clima.ElegiCiudad", "");

    /// <summary>Texto para mostrar cuando no hay datos (sin ciudad, ciudad no encontrada, sin conexion).</summary>
    public string? Problema => _problema is { } p ? Idioma.T(p.Clave, p.Valor) : null;
    public event Action? Cambio;

    public Clima() => _renovacion.Tick += async (_, _) => await ActualizarAsync();

    public string Ciudad
    {
        get => _ciudad;
        set
        {
            _ciudad = value.Trim();
            _renovacion.Stop();
            _renovacion.Start();
            _ = ActualizarAsync();
        }
    }

    public async Task ActualizarAsync()
    {
        if (_ciudad.Length == 0)
        {
            Publicar(null, ("Clima.ElegiCiudad", ""));
            return;
        }

        try
        {
            if (_lugarDe != _ciudad)
            {
                _lugar = await GeocodificarAsync(_ciudad);
                _lugarDe = _ciudad;
            }
            if (_lugar is not { } lugar)
            {
                Publicar(null, ("Clima.NoEncontre", _ciudad));
                return;
            }

            // InvariantCulture: con la configuracion regional en español las coordenadas saldrian con coma
            const string Campos = "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,is_day" +
                                  "&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1";
            string url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={lugar.Latitud}&longitude={lugar.Longitud}{Campos}");
            using var json = JsonDocument.Parse(await Http.GetStringAsync(url));
            var actual = json.RootElement.GetProperty("current");
            var diario = json.RootElement.GetProperty("daily");

            Publicar(new EstadoClima(
                lugar.Nombre,
                actual.GetProperty("temperature_2m").GetDouble(),
                actual.GetProperty("apparent_temperature").GetDouble(),
                actual.GetProperty("weather_code").GetInt32(),
                actual.GetProperty("is_day").GetInt32() == 1,
                actual.GetProperty("relative_humidity_2m").GetInt32(),
                actual.GetProperty("wind_speed_10m").GetDouble(),
                diario.GetProperty("temperature_2m_max")[0].GetDouble(),
                diario.GetProperty("temperature_2m_min")[0].GetDouble()), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            Log.Escribir($"clima: {ex.Message}");
            // Si ya habia datos se dejan los ultimos; si no, se avisa
            if (Estado is null) Publicar(null, ("Clima.SinConexion", ""));
        }
    }

    private static async Task<(double, double, string)?> GeocodificarAsync(string ciudad)
    {
        string url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(ciudad)}&count=1&language={Idioma.Actual}";
        using var json = JsonDocument.Parse(await Http.GetStringAsync(url));
        if (!json.RootElement.TryGetProperty("results", out var resultados) || resultados.GetArrayLength() == 0) return null;

        var r = resultados[0];
        return (r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), r.GetProperty("name").GetString() ?? ciudad);
    }

    private void Publicar(EstadoClima? estado, (string Clave, string Valor)? problema)
    {
        Estado = estado;
        _problema = problema;
        Cambio?.Invoke();
    }

    /// <summary>Codigo WMO de Open-Meteo → descripcion (en el idioma actual) y simbolo (Segoe UI Symbol).</summary>
    public static (string Texto, string Simbolo) Describir(int codigo, bool esDeDia)
    {
        var (clave, simbolo) = codigo switch
        {
            0 => ("Clima.Despejado", esDeDia ? "☀" : "☾"),
            1 => ("Clima.MayormenteDespejado", esDeDia ? "☀" : "☾"),
            2 => ("Clima.ParcialmenteNublado", "⛅"),
            3 => ("Clima.Nublado", "☁"),
            45 or 48 => ("Clima.Niebla", "☁"),
            >= 51 and <= 57 => ("Clima.Llovizna", "☂"),
            >= 61 and <= 67 => ("Clima.Lluvia", "☂"),
            >= 71 and <= 77 => ("Clima.Nieve", "❄"),
            >= 80 and <= 82 => ("Clima.Chaparrones", "☂"),
            85 or 86 => ("Clima.Nevadas", "❄"),
            >= 95 => ("Clima.Tormenta", "⛈"),
            _ => ("", "☁"),
        };
        return (clave.Length == 0 ? "—" : Idioma.T(clave), simbolo);
    }
}
