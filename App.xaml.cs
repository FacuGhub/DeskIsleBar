using System.Windows;
using DeskIsleBar.Modulos;

namespace DeskIsleBar;

public partial class App : Application
{
    private Mutex? _instancia;
    private Baterias? _baterias;
    private AvisosSistema? _avisosSistema;
    private Volumen? _volumen;
    private Temporizador? _temporizador;
    private Dispositivos? _dispositivos;
    private NotificacionesWindows? _notificaciones;

    // Red de seguridad: un error inesperado se anota y la isla sigue. Si el mismo error se repite sin parar
    // (ej. una animacion rota que falla en cada cuadro) se deja cerrar, para no quedar trabada gastando CPU.
    private string? _ultimoError;
    private int _repeticiones;
    private DateTime _primeraRepeticion;

    private void AlErrorNoManejado(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        string clave = $"{e.Exception.GetType().Name}: {e.Exception.Message}";
        if (clave == _ultimoError && DateTime.Now - _primeraRepeticion < TimeSpan.FromSeconds(10))
        {
            if (++_repeticiones > 10)
            {
                Log.Escribir($"error repetido >10 veces en 10 s, se cierra: {clave}");
                return;   // sin Handled: el proceso termina
            }
        }
        else
        {
            _ultimoError = clave;
            _repeticiones = 1;
            _primeraRepeticion = DateTime.Now;
            Log.Escribir($"error no manejado: {e.Exception}");
        }
        e.Handled = true;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += AlErrorNoManejado;

        // Dibujar sin la placa de video. La ventana es transparente (layered): con la placa WPF dibuja en
        // Direct3D y despues igual copia todo por CPU, y carga la memoria del driver. Medido 28/10:
        // 179 → 57 MB de memoria propia y la mitad de CPU. Para una isla chica el dibujo por software sobra.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        // Diagnostico: prueba cada fuente, abre el informe y se cierra (no necesita que la isla este abierta ni la toca)
        if (e.Args.Contains("--diagnostico"))
        {
            base.OnStartup(e);
            Idioma.Aplicar(Config.Cargar().Idioma);
            Dispatcher.InvokeAsync(async () =>
            {
                try { await Diagnostico.EjecutarAsync(); }
                catch (Exception ex) { Log.Escribir($"diagnostico: {ex}"); }
                Shutdown();
            });
            return;
        }

        var pedido = PedidoAviso.DesdeArgumentos(e.Args);

        // Una sola isla a la vez. Si ya hay una corriendo, esta instancia solo le pasa el aviso y se cierra.
        _instancia = new Mutex(true, @"Local\DeskIsleBar", out bool esPrimera);
        if (!esPrimera)
        {
            if (pedido is not null) Canal.Enviar(pedido);
            Shutdown();
            return;
        }

        // Capturas y videos: datos de ejemplo en vez de los del usuario (no se guarda nada)
        Demo.Activo = e.Args.Contains("--demo");

        base.OnStartup(e);

        var ventana = new MainWindow();
        MainWindow = ventana;
        ventana.Show();

        Canal.Escuchar(Dispatcher, ventana.MostrarAviso);

        _baterias = new Baterias();
        _baterias.Aviso += ventana.MostrarAviso;
        _baterias.Iniciar();

        _avisosSistema = new AvisosSistema(Dispatcher);
        _avisosSistema.Aviso += ventana.MostrarAviso;
        ventana.ConectarIndicadores(_avisosSistema);
        _avisosSistema.Iniciar();

        var musica = new Musica(Dispatcher);
        ventana.ConectarMusica(musica);
        _ = musica.IniciarAsync();

        _volumen = new Volumen(Dispatcher);
        ventana.ConectarVolumen(_volumen);
        _volumen.Iniciar();

        _temporizador = new Temporizador();
        ventana.ConectarTemporizador(_temporizador);

        ventana.ConectarClima(new Clima());
        ventana.ConectarCalendario(new Calendario());

        _dispositivos = new Dispositivos(Dispatcher);
        ventana.ConectarDispositivos(_dispositivos);
        _dispositivos.Iniciar();

        _notificaciones = new NotificacionesWindows(Dispatcher);
        ventana.ConectarNotificaciones(_notificaciones);
        _notificaciones.Iniciar();

        if (pedido is not null) ventana.MostrarAviso(pedido.ComoAviso());
        // Para capturas y pruebas: otro idioma solo en esta ejecucion (no se guarda en el config)
        int idioma = Array.IndexOf(e.Args, "--idioma");
        if (idioma >= 0 && idioma + 1 < e.Args.Length) Idioma.Aplicar(e.Args[idioma + 1]);
        int panel = Array.IndexOf(e.Args, "--panel");
        if (panel >= 0 && panel + 1 < e.Args.Length) ventana.AbrirPanel(e.Args[panel + 1]);

        // Para probar como se ven los avisos de bateria sin esperar a que se descargue un control
        if (e.Args.Contains("--probar"))
        {
            ventana.MostrarAviso(Baterias.AvisoDe(Idioma.T("Bat.ControlXbox"), NivelBateria.Bajo, null, Baterias.IconoControl));
            ventana.MostrarAviso(Baterias.AvisoDe("WH-1000XM5", NivelBateria.Critico, 8, Baterias.IconoBluetooth));
        }
    }
}
