using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DeskIsleBar.Modulos;

public record EstadoMusica(string Titulo, string Artista, BitmapImage? Portada, bool Reproduciendo,
                           bool PuedeAnterior, bool PuedeSiguiente, bool PuedeSaltar);

/// <summary>
/// Lee lo que esta sonando desde los controles multimedia de Windows (SMTC) y permite controlarlo.
/// En automatico sigue a la app que este reproduciendo; con una preferida, la usa siempre que este abierta.
/// </summary>
public class Musica
{
    /// <summary>
    /// Apps elegibles en Opciones. Nombre: clave de idioma si empieza con "Musica.", si no el nombre tal cual.
    /// Ids = fragmentos del AppUserModelId que las identifican.
    /// </summary>
    public static readonly (string Clave, string Nombre, string[] Ids)[] Apps =
    [
        ("auto", "Musica.Auto", []),
        // Volumen: el de su salida (modo exclusivo, ver Volumen.ModoExclusivo)
        ("tidal", "Musica.TidalExclusivo", ["tidal"]),
        ("spotify", "Spotify", ["spotify"]),
        // App de th-ch y la PWA de YouTube Music instalada desde Chrome/Edge (id de la extension). Chrome
        // recorta el id en el AppUserModelId: "Chrome._crx_cinhimbnkkghhklpknlkffjgod" (sin "aeohf")
        ("ytmusic", "YouTube Music", ["youtube-music", "youtubemusic", "cinhimbnkkaeohfgghhklpknlkffjgod", "cinhimbnkkghhklpknlkffjgod"]),
        ("applemusic", "Apple Music", ["applemusic"]),
    ];

    private readonly Dispatcher _ui;
    private GlobalSystemMediaTransportControlsSessionManager? _gestor;
    private GlobalSystemMediaTransportControlsSession? _sesion;
    // Windows no avisa cuando OTRA app empieza a sonar si no es la "actual": se revisa cada tanto
    private readonly DispatcherTimer _revision = new() { Interval = TimeSpan.FromSeconds(2) };
    private string _preferida = "auto";

    /// <summary>Clave de <see cref="Apps"/>. Al cambiarla se vuelve a elegir la sesion.</summary>
    public string Preferida
    {
        get => _preferida;
        set
        {
            _preferida = value;
            ElegirSesion();
        }
    }

    // La portada solo se recarga cuando cambia el tema, no en cada play/pausa
    private string? _clavePortada;
    private BitmapImage? _portada;
    private byte[]? _huellaPortada;
    // Segunda mirada a la portada 2 s despues de cada tema nuevo (apps que la cambian sin avisar)
    private readonly DispatcherTimer _revisarPortada = new() { Interval = TimeSpan.FromSeconds(2) };

    public EstadoMusica? Estado { get; private set; }
    /// <summary>AppUserModelId de la app que se esta mostrando (ej. "com.squirrel.TIDAL.TIDAL").</summary>
    public string? App => _sesion?.SourceAppUserModelId;
    public event Action<EstadoMusica?>? Cambio;
    /// <summary>Empezo otro tema (no se dispara con el primero que se lee al arrancar).</summary>
    public event Action? TemaNuevo;

    public Musica(Dispatcher ui) => _ui = ui;

    /// <summary>
    /// Nombre de la app (y de su proceso) a partir del AppUserModelId:
    /// "com.squirrel.TIDAL.TIDAL" → "TIDAL"; "Spotify.exe" → "Spotify";
    /// "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify" (app de la Store) → "Spotify".
    /// </summary>
    public static string NombreDeApp(string appUserModelId)
    {
        string id = appUserModelId.Contains('!') ? appUserModelId[(appUserModelId.IndexOf('!') + 1)..] : appUserModelId;
        if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) id = id[..^4];
        return id[(id.LastIndexOf('.') + 1)..];
    }

    /// <summary>
    /// Proceso que reproduce el audio de la app. Las apps instaladas desde el navegador suenan dentro de el:
    /// "Chrome._crx_cinhimbnkk..." (YouTube Music) → "Chrome".
    /// </summary>
    public static string ProcesoDeApp(string appUserModelId)
    {
        int crx = appUserModelId.IndexOf("._crx_", StringComparison.OrdinalIgnoreCase);
        return crx > 0 ? appUserModelId[..crx] : NombreDeApp(appUserModelId);
    }

    public async Task IniciarAsync()
    {
        try
        {
            _gestor = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception ex)
        {
            Log.Escribir($"musica: no se pudo abrir SMTC: {ex.Message}");
            return;
        }

        // Los eventos de WinRT llegan en otros hilos: todo se procesa en el hilo de la interfaz
        _gestor.SessionsChanged += (_, _) => _ui.InvokeAsync(ElegirSesion);
        _gestor.CurrentSessionChanged += (_, _) => _ui.InvokeAsync(ElegirSesion);
        _revision.Tick += (_, _) => ElegirSesion();
        _revisarPortada.Tick += async (_, _) => { _revisarPortada.Stop(); await ActualizarAsync(releerPortada: true); };
        _revision.Start();
        ElegirSesion();
    }

    private void ElegirSesion()
    {
        if (_gestor is null) return;

        var nueva = Candidata(_gestor.GetSessions());
        if (nueva is not null && nueva.SourceAppUserModelId == _sesion?.SourceAppUserModelId) return;   // sigue la misma
        if (nueva is null && _sesion is null) return;

        if (_sesion is not null)
        {
            _sesion.MediaPropertiesChanged -= AlCambiarPropiedades;
            _sesion.PlaybackInfoChanged -= AlCambiarReproduccion;
        }

        _sesion = nueva;
        if (_sesion is not null)
        {
            _sesion.MediaPropertiesChanged += AlCambiarPropiedades;
            _sesion.PlaybackInfoChanged += AlCambiarReproduccion;
        }

        _ = ActualizarAsync();
    }

    /// <summary>
    /// 1) la preferida, si esta abierta; 2) la actual si sigue sonando (no saltar entre dos que suenan);
    /// 3) cualquiera que este sonando; 4) la actual aunque este en pausa (no saltar al pausar);
    /// 5) la que Windows considere actual.
    /// </summary>
    private GlobalSystemMediaTransportControlsSession? Candidata(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sesiones)
    {
        var ids = Apps.FirstOrDefault(a => a.Clave == _preferida).Ids ?? [];
        if (ids.Length > 0 && sesiones.FirstOrDefault(s => Coincide(s, ids)) is { } preferida) return preferida;

        static bool Suena(GlobalSystemMediaTransportControlsSession s)
        {
            try { return s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
            catch (Exception) { return false; }
        }

        var actual = sesiones.FirstOrDefault(s => s.SourceAppUserModelId == _sesion?.SourceAppUserModelId);
        if (actual is not null && Suena(actual)) return actual;
        return sesiones.FirstOrDefault(Suena) ?? actual ?? _gestor?.GetCurrentSession();
    }

    private static bool Coincide(GlobalSystemMediaTransportControlsSession sesion, string[] ids) =>
        ids.Any(id => sesion.SourceAppUserModelId.Contains(id, StringComparison.OrdinalIgnoreCase));

    // Datos del tema (titulo, artista, portada): puede cambiar solo la portada con el mismo titulo
    private void AlCambiarPropiedades(GlobalSystemMediaTransportControlsSession sesion, object args) =>
        _ui.InvokeAsync(() => ActualizarAsync(releerPortada: true));

    // Play/pausa y controles: la portada no cambia
    private void AlCambiarReproduccion(GlobalSystemMediaTransportControlsSession sesion, object args) =>
        _ui.InvokeAsync(() => ActualizarAsync());

    /// <param name="releerPortada">
    /// Volver a leer la portada aunque el tema sea el mismo. Muchas apps (Tidal entre ellas) avisan el tema nuevo en
    /// dos pasos: primero el titulo y un instante despues la portada; si solo se leyera al cambiar el titulo, quedaria
    /// la del tema anterior (o ninguna).
    /// </param>
    private async Task ActualizarAsync(bool releerPortada = false)
    {
        var sesion = _sesion;
        if (sesion is null)
        {
            Publicar(null);
            return;
        }

        try
        {
            var propiedades = await sesion.TryGetMediaPropertiesAsync();
            var reproduccion = sesion.GetPlaybackInfo();

            string clave = $"{sesion.SourceAppUserModelId}|{propiedades.Artist}|{propiedades.Title}";
            bool cambioDeTema = clave != _clavePortada;
            bool temaNuevo = cambioDeTema && _clavePortada is not null;
            if (cambioDeTema || releerPortada)
            {
                var (portada, huella) = propiedades.Thumbnail is { } miniatura ? await CargarPortadaAsync(miniatura) : (null, null);
                // Misma imagen que la que ya esta: no se reemplaza (evita redibujar la portada en cada aviso)
                if (cambioDeTema || !MismaHuella(huella, _huellaPortada))
                {
                    _portada = portada;
                    _huellaPortada = huella;
                }
                if (cambioDeTema)
                {
                    _clavePortada = clave;
                    // Por si la app cambia la portada sin avisar: una segunda mirada un rato despues
                    _revisarPortada.Stop();
                    _revisarPortada.Start();
                }
            }

            if (sesion != _sesion) return;   // cambio de sesion mientras esperabamos

            if (string.IsNullOrWhiteSpace(propiedades.Title))
            {
                Publicar(null);
                return;
            }

            Publicar(new EstadoMusica(
                propiedades.Title,
                propiedades.Artist,
                _portada,
                reproduccion.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                reproduccion.Controls.IsPreviousEnabled,
                reproduccion.Controls.IsNextEnabled,
                reproduccion.Controls.IsPlaybackPositionEnabled));

            if (temaNuevo && Estado?.Reproduciendo == true) TemaNuevo?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Escribir($"musica: error leyendo la sesion: {ex.Message}");
        }
    }

    private void Publicar(EstadoMusica? estado)
    {
        Estado = estado;
        Cambio?.Invoke(estado);
    }

    private static bool MismaHuella(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);

    /// <summary>La portada lista para mostrar y su huella (SHA-256 de los bytes), para saber si cambio.</summary>
    private static async Task<(BitmapImage? Imagen, byte[]? Huella)> CargarPortadaAsync(IRandomAccessStreamReference miniatura)
    {
        try
        {
            using var origen = await miniatura.OpenReadAsync();
            using var lectura = origen.AsStream();
            var memoria = new MemoryStream();
            await lectura.CopyToAsync(memoria);
            byte[] huella = System.Security.Cryptography.SHA256.HashData(memoria.ToArray());
            memoria.Position = 0;

            var imagen = new BitmapImage();
            imagen.BeginInit();
            imagen.CacheOption = BitmapCacheOption.OnLoad;
            imagen.DecodePixelWidth = 128;
            imagen.StreamSource = memoria;
            imagen.EndInit();
            imagen.Freeze();
            return (imagen, huella);
        }
        catch (Exception ex)
        {
            Log.Escribir($"musica: no se pudo cargar la portada: {ex.Message}");
            return (null, null);
        }
    }

    /// <summary>
    /// Posicion y duracion del tema. La app solo informa la posicion cada tanto, asi que mientras
    /// suena se extrapola desde la ultima actualizacion. null si la app no publica la linea de tiempo.
    /// </summary>
    public (TimeSpan Posicion, TimeSpan Duracion)? Progreso()
    {
        if (_sesion is null) return null;
        try
        {
            var linea = _sesion.GetTimelineProperties();
            var duracion = linea.EndTime - linea.StartTime;
            if (duracion <= TimeSpan.Zero) return null;

            var posicion = linea.Position - linea.StartTime;
            if (Estado?.Reproduciendo == true) posicion += DateTimeOffset.Now - linea.LastUpdatedTime;
            if (posicion < TimeSpan.Zero) posicion = TimeSpan.Zero;
            if (posicion > duracion) posicion = duracion;
            return (posicion, duracion);
        }
        catch (Exception) { return null; }
    }

    /// <summary>Salta a una parte del tema (0 = principio, 1 = final).</summary>
    public Task SaltarAsync(double fraccion) => Ejecutar(s =>
    {
        var linea = s.GetTimelineProperties();
        var destino = linea.StartTime + (linea.EndTime - linea.StartTime) * Math.Clamp(fraccion, 0, 1);
        return s.TryChangePlaybackPositionAsync(destino.Ticks).AsTask();
    });

    public Task AnteriorAsync() => Ejecutar(s => s.TrySkipPreviousAsync().AsTask());
    public Task ReproducirPausarAsync() => Ejecutar(s => s.TryTogglePlayPauseAsync().AsTask());
    public Task SiguienteAsync() => Ejecutar(s => s.TrySkipNextAsync().AsTask());

    private async Task Ejecutar(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> accion)
    {
        if (_sesion is null) return;
        try { await accion(_sesion); }
        catch (Exception ex) { Log.Escribir($"musica: fallo un control: {ex.Message}"); }
    }
}
