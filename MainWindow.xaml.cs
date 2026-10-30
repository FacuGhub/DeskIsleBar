using System.Globalization;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using DeskIsleBar.Modulos;
using Microsoft.Win32;

namespace DeskIsleBar;

public partial class MainWindow : Window
{
    private enum Modo { Reposo, Aviso, Musica, Volumen, Herramientas, Opciones, Calendario, Clima, Pc, Notas, Tutorial, Calendarios, Inicio, Avisos, Bienvenida }

    private const double DistanciaGesto = 60;   // px de arrastre horizontal para cambiar de panel

    private readonly Config _config = Config.Cargar();

    /// <summary>Paneles que se abren con el mouse y se cierran al sacarlo.</summary>
    private bool EnPanel => _modo is not (Modo.Reposo or Modo.Aviso);

    /// <summary>
    /// La ronda de paneles que se recorre arrastrando (ciclica, en las dos direcciones).
    /// Primero Musica si algo esta sonando, si no el Reloj (hora + lo ultimo escuchado);
    /// los demas segun los interruptores de Opciones; Accesos siempre.
    /// </summary>
    private Modo[] Paginas
    {
        get
        {
            var paginas = new List<Modo>();
            paginas.Add(_musica?.Estado?.Reproduciendo == true ? Modo.Musica : Modo.Inicio);
            if (_config.PanelCalendario) paginas.Add(Modo.Calendario);
            if (_config.PanelClima) paginas.Add(Modo.Clima);
            if (_config.PanelPc) paginas.Add(Modo.Pc);
            if (_config.PanelNotas) paginas.Add(Modo.Notas);
            paginas.Add(Modo.Herramientas);
            return [.. paginas];
        }
    }

    private const int MaxCola = 10;

    private IntPtr _hwnd;
    private Modo _modo = Modo.Reposo;
    private bool _ocultaPorPantallaCompleta;   // juego a pantalla completa
    private bool _conVideo;   // video a pantalla completa: oculta solo en reposo
    private bool _avisoFijo;   // el aviso a la vista es persistente
    private Action? _alTocarAviso;   // lo que hace el clic en el aviso a la vista (ej. abrir el chat)
    // El recorte de Windows escribe el portapapeles varias veces seguidas: se espera a que se quede quieto
    private readonly DispatcherTimer _portapapeles = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly List<Aviso> _retenidos = [];   // modo foco: avisos que esperan al fin del bloque de estudio
    private readonly Dictionary<Modo, Modo> _volverA = new();   // pantalla con flecha de volver → panel de origen
    private bool _volviendo;
    private readonly Queue<Aviso> _cola = new();   // avisos que esperan a que termine el actual o a salir de pantalla completa
    private Musica? _musica;
    private Temporizador? _temporizador;
    private Dispositivos? _dispositivos;
    private NotificacionesWindows? _notificaciones;
    private Volumen? _volumen;
    private Clima? _clima;
    private Calendario? _calendario;
    private readonly EstadoPc _estadoPc = new();
    private bool _leyendoPc;
    // La isla no toma el foco (no saca al juego de primer plano). Para escribir en las notas u opciones se
    // le permite tomarlo mientras dure la edicion; al hacer clic en otro lado lo devuelve y se cierra.
    private bool _escribiendo;
    private Grid? _pistaArrastrada;      // barra de volumen o de progreso que se esta arrastrando
    private Modo? _volverDeVolumen;      // panel desde el que se abrio el volumen: al terminar se vuelve ahi
    private bool _volumenDeApp;          // el panel de volumen muestra el de la app de musica (no el general)
    // Gesto sobre los paneles: arrastrar = cambiar de panel; clic sin arrastrar en portada/tema = abrir la app
    private Point? _inicioGesto;
    private bool _gestoMovido;
    private bool _gestoSobreTema;
    private TextBox? _campoPendiente;    // campo de texto apretado sin estar editando: clic = editar, arrastre = cambiar de panel
    // El arrastre cambia de panel con el boton todavia apretado: el "soltar" cae en el panel nuevo y, si hay un
    // evento o un dia debajo, lo tomaria como clic. Este soltar es del gesto y se descarta.
    private bool _soltarDelGesto;
    // Tamaño al que va (o llego) el reposo. No se lee Pildora.Width porque en medio de una animacion miente.
    private (double Ancho, double Alto) _reposoDestino = (96, 10);

    private readonly DispatcherTimer _vigilancia = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _cierre = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly DispatcherTimer _cierreVolumen = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly DispatcherTimer _cierreTemaNuevo = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _progreso = new() { Interval = TimeSpan.FromMilliseconds(500) };
    // Al cambiar de tema la app avisa "pausado" y enseguida "sonando": se espera a que se estabilice
    private readonly DispatcherTimer _ajusteReposo = new() { Interval = TimeSpan.FromMilliseconds(400) };
    // Demoras del hover: no abrir por pasar de largo hacia las pestañas, no cerrar por un desliz del mouse
    private readonly DispatcherTimer _entrada = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _salida = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _lecturaPc = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _reloj = new() { Interval = TimeSpan.FromSeconds(1) };
    private EstadoMusica? _ultimoEscuchado;   // queda aunque la app de musica se cierre
    private readonly DispatcherTimer _guardarNotas = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _buscarActualizacion = new() { Interval = TimeSpan.FromSeconds(20) };
    private bool _actualizando;

    public MainWindow()
    {
        Idioma.Aplicar(_config.Idioma);   // antes de dibujar: los textos del XAML salen del diccionario
        InitializeComponent();
        ArmarChipsMusica();
        ArmarChipsIdioma();
        ArmarChipsPrimerPlano();
        ArmarInterruptoresAvisos();
        PrepararBienvenida();
        Idioma.Cambio += AlCambiarIdioma;
        _buscarActualizacion.Tick += async (_, _) =>
        {
            _buscarActualizacion.Interval = TimeSpan.FromHours(24);   // la primera a los 20 s, despues una por dia
            await BuscarActualizacionAsync(aPedido: false);
        };
        if (!Demo.Activo) _buscarActualizacion.Start();

        CampoNotas.Text = Demo.Activo ? Demo.Nota : Notas.Leer();
        CampoCiudad.Text = Demo.Activo ? Demo.Ciudad : _config.Ciudad;

        _lecturaPc.Tick += async (_, _) => await LeerPcAsync();
        _reloj.Tick += (_, _) => ActualizarHora();
        _finLimpieza.Tick += (_, _) => ReiniciarLimpieza();
        _reloj.Start();
        ActualizarHora();
        if (_config.PanelPc) _estadoPc.Precalentar();
        _guardarNotas.Tick += (_, _) =>
        {
            _guardarNotas.Stop();
            Notas.Guardar(CampoNotas.Text);
        };

        Pildora.SizeChanged += (_, _) => Pildora.CornerRadius = new CornerRadius(Math.Min(Pildora.ActualHeight / 2, 26));
        _cierre.Tick += (_, _) => Colapsar();
        _cierreVolumen.Tick += (_, _) =>
        {
            _cierreVolumen.Stop();
            if (_modo != Modo.Volumen) return;
            if (Zona.IsMouseOver)
            {
                // Con el mouse encima no se cierra (se cierra al salir); si vino de un panel, vuelve a el
                if (_pistaArrastrada is null && _volverDeVolumen is { } panel && (panel != Modo.Musica || _musica?.Estado is not null))
                    Expandir(panel);
                return;
            }
            Colapsar();
        };
        _cierreTemaNuevo.Tick += (_, _) =>
        {
            _cierreTemaNuevo.Stop();
            if (_modo == Modo.Musica && !Zona.IsMouseOver) Colapsar();
        };
        _progreso.Tick += (_, _) => ActualizarProgreso();
        _ajusteReposo.Tick += (_, _) =>
        {
            _ajusteReposo.Stop();
            if (_modo == Modo.Reposo && TamañoReposo != _reposoDestino) Colapsar();
        };
        _vigilancia.Tick += (_, _) => Vigilar();
        _entrada.Tick += (_, _) =>
        {
            _entrada.Stop();
            if (_modo == Modo.Reposo) Expandir(Paginas[0]);
        };
        _salida.Tick += (_, _) =>
        {
            _salida.Stop();
            if (EnPanel && _inicioGesto is null && _modo != Modo.Bienvenida) Colapsar();
        };
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.Invoke(Ubicar);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // Fuera de Alt+Tab y sin robar el foco (no saca al juego de primer plano)
        int estilo = Nativo.GetWindowLong(_hwnd, Nativo.GWL_EXSTYLE);
        estilo = (estilo | Nativo.WS_EX_TOOLWINDOW | Nativo.WS_EX_NOACTIVATE) & ~Nativo.WS_EX_APPWINDOW;
        Nativo.SetWindowLong(_hwnd, Nativo.GWL_EXSTYLE, estilo);

        // Mensajes de Windows: los pendrives (modulo de dispositivos) y los cambios del portapapeles (captura copiada)
        HwndSource.FromHwnd(_hwnd).AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            _dispositivos?.ProcesarMensaje(msg, wParam, lParam);
            if (msg == Nativo.WM_CLIPBOARDUPDATE) { _portapapeles.Stop(); _portapapeles.Start(); }
            return IntPtr.Zero;
        });
        Nativo.AddClipboardFormatListener(_hwnd);
        _portapapeles.Tick += (_, _) => { _portapapeles.Stop(); RevisarPortapapeles(); };

        Ubicar();
        _vigilancia.Start();
    }

    /// <summary>Si lo que se copio es una imagen (Win+Shift+S, Impr Pant, "copiar imagen"), avisa que esta lista para pegar.</summary>
    private void RevisarPortapapeles()
    {
        bool imagen;
        try { imagen = Clipboard.ContainsImage(); }
        catch (System.Runtime.InteropServices.COMException) { return; }   // otra app lo tiene abierto en ese instante
        if (!imagen) return;
        MostrarAviso(new Aviso("", Idioma.T("Av.ImagenCopiada"), Idioma.T("Av.ImagenCopiadaDetalle"),
                               Color.FromRgb(0x0A, 0x84, 0xFF), Carga: null, Persistente: false, Segundos: 2.5, Tipo: TiposAviso.Captura));
    }

    /// <summary>Donde la dejo el usuario (con su anclaje); si nunca la movio, arriba al centro del principal.</summary>
    private void Ubicar()
    {
        if (Posicion.Leer() is { } lugar)
        {
            Anclar(lugar.Horizontal, lugar.Vertical);
            Left = lugar.Left;
            Top = lugar.Top;
        }
        else
        {
            Anclar(HorizontalAlignment.Center, VerticalAlignment.Top);
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = 0;
        }
        MeterEnPantalla();   // ej. si se desconecto el monitor donde estaba
    }

    /// <summary>
    /// Hacia donde crece la pildora dentro de la ventana (invisible, 480x390; el alto tiene que alcanzar para el
    /// panel mas alto + 12 de margen, si no se corta): pegada arriba crece hacia abajo,
    /// pegada abajo crece hacia arriba; a la izquierda crece hacia la derecha y viceversa.
    /// </summary>
    private void Anclar(HorizontalAlignment horizontal, VerticalAlignment vertical)
    {
        Zona.HorizontalAlignment = horizontal;
        Zona.VerticalAlignment = vertical;
        // Misma alineacion adentro de la zona: si la zona queda mas grande que la pildora, la pildora sigue pegada al borde
        Pildora.HorizontalAlignment = horizontal;
        Pildora.VerticalAlignment = vertical;
        bool abajo = vertical == VerticalAlignment.Bottom;
        Pildora.Margin = new Thickness(14, abajo ? 8 : 4, 14, abajo ? 4 : 8);   // el margen chico, del lado del borde
        // Las flechas (afuera de la pildora) centradas en su alto
        FlechaIzquierda.Margin = FlechaDerecha.Margin = new Thickness(0, Pildora.Margin.Top, 0, Pildora.Margin.Bottom);
    }

    /// <summary>
    /// Despues de arrastrarla: elige el anclaje segun en que zona de la pantalla quedo la pildora (mitad de abajo →
    /// crece hacia arriba; tercio izquierdo/derecho → crece hacia el otro lado) y acomoda la ventana para que la
    /// pildora quede exactamente donde se solto.
    /// </summary>
    private void Reorientar()
    {
        var area = AreaDeTrabajo();
        var esquina = Pildora.TranslatePoint(new Point(0, 0), this);
        double x = Left + esquina.X, y = Top + esquina.Y;
        double ancho = Pildora.ActualWidth, alto = Pildora.ActualHeight;
        double centroX = x + ancho / 2, centroY = y + alto / 2;

        var horizontal = centroX < area.Left + area.Width / 3 ? HorizontalAlignment.Left
                       : centroX > area.Right - area.Width / 3 ? HorizontalAlignment.Right
                       : HorizontalAlignment.Center;
        var vertical = centroY > area.Top + area.Height / 2 ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        Anclar(horizontal, vertical);

        Left = horizontal switch
        {
            HorizontalAlignment.Left => x - Pildora.Margin.Left,
            HorizontalAlignment.Right => x + ancho + Pildora.Margin.Right - Width,
            _ => centroX - Width / 2,
        };
        Top = vertical == VerticalAlignment.Bottom ? y + alto + Pildora.Margin.Bottom - Height : y - Pildora.Margin.Top;
        MeterEnPantalla();
        Posicion.Guardar(new Posicion.Lugar(Left, Top, horizontal, vertical));
    }

    /// <summary>La ventana entera dentro del area de trabajo: asi el panel mas grande nunca se sale de la pantalla.</summary>
    private void MeterEnPantalla()
    {
        var area = AreaDeTrabajo();
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }

    /// <summary>Area de trabajo (sin barra de tareas) del monitor donde esta la isla, en unidades de WPF.</summary>
    private Rect AreaDeTrabajo()
    {
        var r = Nativo.AreaDeTrabajo(_hwnd);
        var aWpf = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return new Rect(aWpf.Transform(new Point(r.Left, r.Top)), aWpf.Transform(new Point(r.Right, r.Bottom)));
    }

    /// <summary>
    /// Juego a pantalla completa: la isla desaparece y los avisos esperan en la cola.
    /// Video a pantalla completa: no tapa nada en reposo, pero aparece para avisos y volumen.
    /// </summary>
    private void Vigilar()
    {
        if (_config.PrimerPlano == "siempre")
        {
            // Encima de todo, tambien de juegos y videos (los de pantalla completa exclusiva no dejan dibujar encima)
            bool estabaOculta = _ocultaPorPantallaCompleta || !IsVisible;
            _ocultaPorPantallaCompleta = false;
            _conVideo = false;
            if (!IsVisible) Show();
            if (!Zona.IsMouseOver) Nativo.ReafirmarTopmost(_hwnd);   // con el mouse encima puede haber un cartelito de ayuda
            if (estabaOculta) MostrarSiguienteDeLaCola();
            IntentarBienvenida();
            return;
        }

        var estado = Nativo.EstadoPantallaCompleta(_hwnd);
        bool juego = estado == Nativo.PantallaCompleta.Juego;
        bool video = estado == Nativo.PantallaCompleta.Video;

        if (juego)
        {
            _conVideo = false;
            if (_ocultaPorPantallaCompleta) return;
            _ocultaPorPantallaCompleta = true;
            if (_modo == Modo.Bienvenida) _bienvenidaPendiente = true;   // se retoma al salir del juego
            if (EnPanel) Colapsar();
            // Un fijo que ya estaba a la vista queda encima del juego hasta que lo toquen
            if (!(_modo == Modo.Aviso && _avisoFijo)) Hide();
            return;
        }

        bool veniaDeJuego = _ocultaPorPantallaCompleta;
        bool empiezaVideo = video && !_conVideo;
        _ocultaPorPantallaCompleta = false;
        _conVideo = video;

        if (video)
        {
            // Si estaba en un panel se oculta recien al volver al reposo (ver Colapsar)
            if ((empiezaVideo || veniaDeJuego) && _modo == Modo.Reposo) Hide();
        }
        else
        {
            if (!IsVisible) Show();
            // Con el mouse encima no se reafirma: la isla le pasaria por encima a su propio cartelito de ayuda
            // (que es otra ventana) y lo taparia. Al abrir y cerrar paneles AplicarCapa se llama igual.
            if (!Zona.IsMouseOver) AplicarCapa();
        }
        if (veniaDeJuego) MostrarSiguienteDeLaCola();
        IntentarBienvenida();
    }

    /// <summary>
    /// Automatico: siempre encima. Detras de las apps: en reposo queda debajo de las ventanas (no tapa pestañas ni
    /// botones); cuando muestra algo (aviso, volumen, un panel) pasa al frente y al volver al reposo se va atras.
    /// </summary>
    private void AplicarCapa()
    {
        bool detras = _config.PrimerPlano == "detras";
        if (Topmost == detras) Topmost = !detras;   // que WPF no vuelva a ponerla encima por su cuenta
        if (_hwnd == IntPtr.Zero) return;
        if (detras && _modo == Modo.Reposo) Nativo.MandarAtras(_hwnd);
        else Nativo.ReafirmarTopmost(_hwnd);
    }

    // ---------- Conexion con los modulos ----------

    public void ConectarMusica(Musica musica)
    {
        _musica = musica;
        musica.Preferida = _config.AppMusica;
        musica.Cambio += MostrarMusica;
        musica.TemaNuevo += AnunciarTemaNuevo;
    }

    public void ConectarTemporizador(Temporizador temporizador)
    {
        _temporizador = temporizador;
        temporizador.Cambio += ActualizarReposo;
        temporizador.Cambio += LiberarRetenidos;   // termino o se cancelo el bloque de estudio
        temporizador.Aviso += MostrarAviso;
    }

    public void ConectarVolumen(Volumen volumen)
    {
        _volumen = volumen;
        volumen.Cambio += MostrarVolumen;
    }

    public void ConectarClima(Clima clima)
    {
        _clima = clima;
        clima.Cambio += () =>
        {
            if (_modo == Modo.Clima) MostrarClima();
            else if (_modo == Modo.Inicio) MostrarInicio();
        };
        clima.Ciudad = Demo.Activo ? Demo.Ciudad : _config.Ciudad;
    }

    public void ConectarCalendario(Calendario calendario)
    {
        _calendario = calendario;
        calendario.Cambio += () => { if (_modo == Modo.Calendario) MostrarCalendario(); };
        if (Demo.Activo) calendario.MostrarDemo(Demo.Eventos());
        else calendario.Fuentes = FuentesDesdeConfig();
        calendario.Recordatorio += e =>
        {
            int minutos = Math.Max(1, (int)Math.Ceiling((e.Inicio - DateTime.Now).TotalMinutes));
            MostrarAviso(new Aviso("", e.Titulo, Idioma.T("Av.RecordatorioDetalle", minutos, e.Inicio.ToString("HH:mm")),
                                   e.Color, Carga: null, Persistente: false, Segundos: 10, Tipo: TiposAviso.Recordatorios));
        };
    }

    public void ConectarDispositivos(Dispositivos dispositivos)
    {
        _dispositivos = dispositivos;
        dispositivos.Aviso += MostrarAviso;
    }

    public void ConectarNotificaciones(NotificacionesWindows notificaciones)
    {
        _notificaciones = notificaciones;
        notificaciones.Aviso += MostrarAviso;
        notificaciones.Activo = _config.NotificacionesWindows;
        notificaciones.RespetarNoMolestar = _config.RespetarNoMolestar;
        notificaciones.OmitirRecortes = !_config.AvisosApagados.Contains(TiposAviso.Captura);
        // Misma app que esta sonando y su texto trae el titulo del tema: es el "reproduciendo ahora" (Apple Music).
        // Las dos condiciones: si suena YouTube en Chrome, las notificaciones de WhatsApp Web (tambien "Chrome") siguen.
        notificaciones.EsAnuncioDeMusica = (app, textos) =>
            _musica?.App is { } suena && _musica.Estado is { Titulo.Length: > 0 } estado &&
            string.Equals(app, suena, StringComparison.OrdinalIgnoreCase) &&
            textos.Any(t => t.Contains(estado.Titulo, StringComparison.OrdinalIgnoreCase));
    }

    // ---------- Avisos ----------

    public void MostrarAviso(Aviso aviso)
    {
        if (aviso.Tipo is { } tipo && _config.AvisosApagados.Contains(tipo)) return;   // apagado en Opciones → Avisos

        // Modo foco: durante el estudio los comunes esperan al descanso; los fijos (importantes) siguen de largo
        if (_config.FocoPomodoro && _temporizador?.Estudiando == true && !aviso.Persistente)
        {
            if (_retenidos.Count < MaxCola) _retenidos.Add(aviso);
            return;
        }

        // Los fijos (bateria critica, fin del temporizador, WHEA) importan: aparecen aunque haya un juego
        // (sin bordes; con pantalla completa exclusiva Windows no deja dibujar encima). Solo esperan si ya hay
        // otro fijo a la vista; un aviso comun que quedo oculto por el juego se pisa.
        if (_ocultaPorPantallaCompleta && aviso.Persistente && !(_modo == Modo.Aviso && IsVisible))
        {
            Show();
            Nativo.ReafirmarTopmost(_hwnd);
        }
        else if (_ocultaPorPantallaCompleta || _modo is Modo.Aviso or Modo.Bienvenida)
        {
            if (_cola.Count < MaxCola) _cola.Enqueue(aviso);
            return;
        }

        Icono.Text = aviso.Imagen is null ? aviso.Icono : "";
        Icono.Foreground = new SolidColorBrush(aviso.Acento);
        IconoApp.Source = aviso.Imagen;
        IconoApp.Visibility = aviso.Imagen is null ? Visibility.Collapsed : Visibility.Visible;
        _alTocarAviso = aviso.AlTocar;
        Pildora.Cursor = aviso.AlTocar is null ? null : Cursors.Hand;
        Titulo.Text = aviso.Titulo;
        Detalle.Text = aviso.Detalle;
        Detalle.Visibility = string.IsNullOrEmpty(aviso.Detalle) ? Visibility.Collapsed : Visibility.Visible;
        Bateria.Visibility = aviso.Carga is null ? Visibility.Collapsed : Visibility.Visible;
        Carga.Background = new SolidColorBrush(aviso.Acento);
        Carga.Width = Math.Max(3, 26 * (aviso.Carga ?? 0));

        Expandir(Modo.Aviso);

        _cierre.Stop();
        _avisoFijo = aviso.Persistente;
        if (aviso.Persistente)
        {
            SystemSounds.Asterisk.Play();   // los fijos importan: que se escuchen aunque no estes mirando
        }
        else
        {
            _cierre.Interval = TimeSpan.FromSeconds(aviso.Segundos);
            _cierre.Start();
        }
    }

    private void MostrarSiguienteDeLaCola()
    {
        if (_modo == Modo.Reposo && !_ocultaPorPantallaCompleta && _cola.TryDequeue(out var siguiente))
            MostrarAviso(siguiente);
    }

    // ---------- Volumen ----------

    // El volumen general (teclas) llega por este metodo desde el modulo; el de la app lo llama la propia ventana
    public void MostrarVolumen(float nivel, bool silenciado) => MostrarVolumen(nivel, silenciado, deApp: false);

    private void MostrarVolumen(float nivel, bool silenciado, bool deApp)
    {
        // Es efimero: si la isla esta oculta o mostrando un aviso, no vale la pena encolarlo
        if (_ocultaPorPantallaCompleta || _modo == Modo.Aviso) return;
        if (_modo == Modo.Bienvenida)
        {
            MostrarVolumenEnBienvenida(nivel, silenciado);
            return;
        }

        _volumenDeApp = deApp;
        int porcentaje = (int)Math.Round(nivel * 100);
        IconoVolumen.Text = silenciado || porcentaje == 0 ? ""   // mute
                          : deApp ? ""                           // nota musical: volumen de la app
                          : porcentaje < 34 ? ""
                          : porcentaje < 67 ? ""
                          : "";
        PorcentajeVolumen.Text = silenciado ? "" : porcentaje.ToString();
        BarraVolumen.Opacity = silenciado ? 0.35 : 1;
        EscalaVolumen.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(nivel, TimeSpan.FromMilliseconds(120)) { EasingFunction = new QuadraticEase() });

        if (_modo != Modo.Volumen)
        {
            _volverDeVolumen = EnPanel ? _modo : null;
            if (EnPanel)
            {
                // La barra es mucho mas chica que el panel: la zona conserva el tamaño del panel mientras dure, asi el
                // mouse sigue "adentro" (no se cierra) y la rueda le sigue llegando a la isla y no a la ventana de abajo
                Zona.MinWidth = Zona.ActualWidth;
                Zona.MinHeight = Zona.ActualHeight;
            }
            Expandir(Modo.Volumen);
        }
        _cierreVolumen.Stop();
        _cierreVolumen.Start();
    }

    // ---------- Musica ----------

    private void MostrarMusica(EstadoMusica? estado)
    {
        if (estado is null)
        {
            if (_modo == Modo.Musica) Colapsar();
            else if (_modo == Modo.Inicio) MostrarInicio();
            ActualizarReposo();
            return;
        }

        TituloMusica.Text = estado.Titulo;
        ArtistaMusica.Text = estado.Artista;
        Portada.Background = estado.Portada is null ? null : new ImageBrush(estado.Portada) { Stretch = Stretch.UniformToFill };
        SinPortada.Visibility = estado.Portada is null ? Visibility.Visible : Visibility.Collapsed;
        BotonPlay.Content = estado.Reproduciendo ? "" : "";   // pausa / play
        BotonAnterior.IsEnabled = estado.PuedeAnterior;
        BotonSiguiente.IsEnabled = estado.PuedeSiguiente;
        PistaProgreso.Cursor = estado.PuedeSaltar ? Cursors.Hand : null;
        ActualizarProgreso();
        ActualizarReposo();

        _ultimoEscuchado = estado;
        if (_modo == Modo.Inicio)
        {
            if (estado.Reproduciendo) Expandir(Modo.Musica, deslizarDesde: 40);   // empezo a sonar (ej. ▶ en el Inicio)
            else MostrarInicio();
        }
    }

    // ---------- Inicio (hora, clima resumido, lo ultimo escuchado) ----------

    private void ActualizarHora()
    {
        var ahora = DateTime.Now;
        string hora = ahora.ToString("HH:mm");
        // Con temporizador y musica, ese lugar es de la cuenta regresiva (la pone ActualizarReposo)
        if (_temporizador?.Activo != true && RelojReposo.Text != hora) RelojReposo.Text = hora;
        if (_modo == Modo.Inicio && HoraInicio.Text != hora) MostrarInicio();
    }

    private void MostrarInicio()
    {
        var ahora = DateTime.Now;
        HoraInicio.Text = ahora.ToString("HH:mm");
        FechaInicio.Text = Capitalizar(ahora.ToString(Idioma.T("Fmt.FechaLarga"), Idioma.Cultura));

        if (_clima?.Estado is { } clima)
        {
            var (texto, simbolo) = Clima.Describir(clima.Codigo, clima.EsDeDia);
            ClimaInicio.Visibility = Visibility.Visible;
            SimboloInicio.Text = simbolo;
            SimboloInicio.Foreground = clima.Codigo <= 1 && clima.EsDeDia
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x0A))
                : new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
            TemperaturaInicio.Text = $"{clima.Temperatura:0}°";
            DescripcionInicio.Text = texto;
        }
        else
        {
            ClimaInicio.Visibility = Visibility.Collapsed;   // sin ciudad o sin conexion: no ocupa lugar
        }

        // La sesion abierta (aunque este en pausa) o lo ultimo que sono antes de cerrar la app
        var ultimo = _musica?.Estado ?? _ultimoEscuchado;
        if (ultimo is null)
        {
            TarjetaUltimo.Visibility = Visibility.Collapsed;
            return;
        }
        TarjetaUltimo.Visibility = Visibility.Visible;
        TituloUltimo.Text = ultimo.Titulo;
        ArtistaUltimo.Text = ultimo.Artista;
        PortadaUltimo.Background = ultimo.Portada is null ? null : new ImageBrush(ultimo.Portada) { Stretch = Stretch.UniformToFill };
        // Retomar solo si la app sigue abierta; si se cerro, el clic en la tarjeta la abre
        RetomarUltimo.Visibility = _musica?.Estado is not null ? Visibility.Visible : Visibility.Collapsed;
        TarjetaUltimo.ToolTip = _musica?.App is not null ? Idioma.T("Comun.AbrirApp") : null;
    }

    /// <summary>Cambio de tema: se muestra unos segundos, como en el celular.</summary>
    private void AnunciarTemaNuevo()
    {
        // Con video tampoco: el navegador que lo reproduce tambien aparece como "musica"
        if (_ocultaPorPantallaCompleta || _conVideo || _modo != Modo.Reposo) return;
        Expandir(Modo.Musica);
        _cierreTemaNuevo.Start();
    }

    private void ActualizarProgreso()
    {
        if (_musica?.Progreso() is not var (posicion, duracion))
        {
            Progreso.Visibility = Visibility.Collapsed;
            return;
        }

        Progreso.Visibility = Visibility.Visible;
        if (_pistaArrastrada == PistaProgreso) return;   // mientras se arrastra manda el mouse
        EscalaProgreso.ScaleX = posicion / duracion;
        TiempoMusica.Text = $"{Formatear(posicion)} / {Formatear(duracion)}";
    }

    private static string Formatear(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private async void Anterior_Click(object sender, RoutedEventArgs e) { if (_musica is not null) await _musica.AnteriorAsync(); }
    private async void Play_Click(object sender, RoutedEventArgs e) { if (_musica is not null) await _musica.ReproducirPausarAsync(); }
    private async void Siguiente_Click(object sender, RoutedEventArgs e) { if (_musica is not null) await _musica.SiguienteAsync(); }

    private void Zona_MouseEnter(object sender, MouseEventArgs e)
    {
        _salida.Stop();
        if (_modo == Modo.Reposo) _entrada.Start();
    }

    private void Zona_MouseLeave(object sender, MouseEventArgs e)
    {
        _entrada.Stop();
        OcultarFlechas();
        if (_escribiendo) return;   // escribiendo no se cierra al sacar el mouse: se cierra al hacer clic afuera
        if (EnPanel && _pistaArrastrada is null && _modo != Modo.Bienvenida) _salida.Start();   // la bienvenida no se cierra sola
    }

    // ---------- Gestos sobre los paneles ----------

    // Se usan los eventos Preview (bajan desde la ventana ANTES de llegar a los botones) para poder empezar
    // el arrastre encima de un boton: si el mouse se mueve, el panel le saca la captura y el boton cancela su clic.
    private void Panel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _soltarDelGesto = false;   // por si el soltar del gesto cayo fuera de un panel: no debe comerse este clic
        if (e.OriginalSource is not DependencyObject origen) return;
        if (PistaProgreso.IsAncestorOf(origen) || PistaVolumen.IsAncestorOf(origen)) return;   // las barras tienen su propio arrastre
        if (MoverEnBienvenida(origen))
        {
            e.Handled = true;
            return;
        }

        _campoPendiente = null;
        if (CampoDeTexto(origen) is { } campo)
        {
            if (campo.IsKeyboardFocused) return;   // ya se esta escribiendo ahi: arrastrar es seleccionar texto
            // Todavia no se edita: el campo no se queda con el clic. Si se suelta sin mover, recien ahi se edita.
            _campoPendiente = campo;
            e.Handled = true;
        }

        _inicioGesto = e.GetPosition(this);
        _gestoMovido = false;
        _gestoSobreTema = ZonaPortada.IsAncestorOf(origen) || InfoTema.IsAncestorOf(origen) ||
                          (TarjetaUltimo.IsAncestorOf(origen) && !RetomarUltimo.IsAncestorOf(origen));
        // Sin Handled (salvo en campos): si es un boton, que reciba el clic normalmente mientras no haya arrastre
    }

    private void Panel_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_inicioGesto is not { } inicio || e.LeftButton != MouseButtonState.Pressed) return;
        double dx = e.GetPosition(this).X - inicio.X;
        var panel = (FrameworkElement)sender;

        if (!_gestoMovido)
        {
            if (Math.Abs(dx) <= 8) return;   // todavia puede ser un clic
            _gestoMovido = true;
            panel.CaptureMouse();            // le saca la captura al boton: ese clic queda cancelado
        }

        // El panel acompaña un poco al mouse, para que se sienta que se "tira" de el
        ((TranslateTransform)panel.RenderTransform).X = Math.Clamp(dx, -DistanciaGesto, DistanciaGesto) * 0.35;

        if (Math.Abs(dx) > DistanciaGesto)
        {
            TerminarGesto(panel);
            _soltarDelGesto = true;
            Pasar(dx > 0 ? -1 : +1);   // arrastrar a la derecha trae el panel de la izquierda (como en el celular)
        }
    }

    /// <summary>Recorre la ronda de paneles: +1 = siguiente, -1 = anterior, dando la vuelta.</summary>
    private void Pasar(int direccion)
    {
        if (_modo == Modo.Bienvenida)
        {
            PasarBienvenida(direccion);
            return;
        }
        double desde = direccion > 0 ? 40 : -40;   // el panel nuevo entra desde el lado hacia el que se arrastro

        // Opciones y el tutorial no estan en la ronda: se sale hacia el panel desde el que se entra
        if (_modo == Modo.Opciones)
        {
            Expandir(Modo.Herramientas, deslizarDesde: desde);
            return;
        }
        if (_modo == Modo.Calendarios)
        {
            Expandir(_origenCalendarios, deslizarDesde: desde);
            return;
        }
        if (_modo == Modo.Tutorial)
        {
            Expandir(_config.PanelCalendario ? Modo.Calendario : Modo.Opciones, deslizarDesde: desde);
            return;
        }

        var paginas = Paginas;
        int actual = Array.IndexOf(paginas, _modo);
        if (actual < 0)
        {
            // El panel actual salio de la ronda (ej. se pauso la musica estando en ella): ir al primero
            Expandir(paginas[0], deslizarDesde: desde);
            return;
        }
        if (paginas.Length < 2) return;   // un solo panel: el gesto solo rebota
        Expandir(paginas[(actual + direccion + paginas.Length) % paginas.Length], deslizarDesde: desde);
    }

    private void Panel_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_soltarDelGesto)
        {
            // Preview baja antes que nadie: con Handled, el evento/dia/boton de abajo ni se entera
            _soltarDelGesto = false;
            e.Handled = true;
            return;
        }
        if (_inicioGesto is null) return;
        bool arrastro = _gestoMovido;
        bool abrir = !arrastro && _gestoSobreTema && _modo is Modo.Musica or Modo.Inicio;
        TerminarGesto((FrameworkElement)sender);

        var campo = _campoPendiente;
        _campoPendiente = null;

        if (arrastro)
        {
            e.Handled = true;
            if (!Zona.IsMouseOver && EnPanel) _salida.Start();   // se solto afuera
        }
        else if (campo is not null)
        {
            // Clic sin arrastre en un campo: tomar el teclado y dejar el cursor al final del texto
            e.Handled = true;
            TomarTeclado();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                campo.Focus();
                campo.CaretIndex = campo.Text.Length;
            });
        }
        else if (abrir)
        {
            e.Handled = true;
            AbrirAppDeMusica();
        }
        // Clic sin arrastre sobre un boton: no se toca, el boton hace lo suyo
    }

    /// <summary>El campo de texto que contiene al elemento apretado, si hay alguno.</summary>
    private static TextBox? CampoDeTexto(DependencyObject origen)
    {
        for (var d = origen; d is not null;
             d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is TextBox campo) return campo;
        }
        return null;
    }

    private void TerminarGesto(FrameworkElement panel)
    {
        _inicioGesto = null;
        if (panel.IsMouseCaptured) panel.ReleaseMouseCapture();
        ((TranslateTransform)panel.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase() });
    }

    /// <summary>
    /// Trae la app al frente. Si su ventana esta abierta o minimizada, directo (instantaneo).
    /// Si esta escondida en la bandeja, se la lanza por su AppUserModelId: arranca una segunda
    /// instancia que le pide a la primera que se muestre (tarda un poco mas).
    /// </summary>
    private void AbrirAppDeMusica()
    {
        if (_musica?.App is not { } app) return;
        Colapsar();
        if (Nativo.TraerAlFrentePorAppId(app) || Nativo.TraerAlFrente(Musica.NombreDeApp(app))) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $@"shell:AppsFolder\{app}")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Escribir($"musica: no se pudo abrir {app}: {ex.Message}");
        }
    }

    // Rueda sobre la isla = volumen de la app que suena (o el general si no hay musica), de a 2 % por "clic"
    private void Zona_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_volumen is null || _modo == Modo.Aviso) return;
        if (RuedaEnBienvenida())
        {
            e.Handled = true;
            return;
        }
        // Sobre un texto que tiene para desplazarse (una nota larga), la rueda es para desplazarlo; en el resto, volumen
        if (e.OriginalSource is DependencyObject origen && CampoDeTexto(origen) is { } campo && campo.ExtentHeight > campo.ViewportHeight) return;
        e.Handled = true;
        float delta = Math.Sign(e.Delta) * 0.02f;

        if (AppDeMusica() is { } app && _volumen.AjustarApp(app, delta) is var (nivel, silenciado))
            MostrarVolumen(nivel, silenciado, deApp: true);
        else
            _volumen.Ajustar(delta);   // el general avisa solo por su evento
    }

    private string? AppDeMusica() => _musica?.Estado is not null ? _musica.App : null;

    // ---------- Barras tocables (volumen y progreso) ----------

    private void Pista_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;   // que no lo tome la zona (arrastrar la isla) ni la pildora
        var pista = (Grid)sender;
        if (pista == PistaProgreso && _musica?.Estado?.PuedeSaltar != true) return;

        _pistaArrastrada = pista;
        pista.CaptureMouse();
        AplicarPista(pista, e, soltando: false);
    }

    private void Pista_MouseMove(object sender, MouseEventArgs e)
    {
        if (_pistaArrastrada == sender) AplicarPista(_pistaArrastrada, e, soltando: false);
    }

    private void Pista_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pistaArrastrada != sender) return;
        var pista = _pistaArrastrada;
        AplicarPista(pista, e, soltando: true);
        _pistaArrastrada = null;
        pista.ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <summary>El volumen cambia en vivo mientras se arrastra; el tema salta recien al soltar.</summary>
    private void AplicarPista(Grid pista, MouseEventArgs e, bool soltando)
    {
        double fraccion = Math.Clamp(e.GetPosition(pista).X / pista.ActualWidth, 0, 1);

        if (pista == PistaVolumen)
        {
            // La barra maneja el mismo volumen que esta mostrando (el de la app o el general)
            if (_volumenDeApp && AppDeMusica() is { } app && _volumen?.FijarApp(app, (float)fraccion) is var (nivel, silenciado))
                MostrarVolumen(nivel, silenciado, deApp: true);
            else
                _volumen?.Fijar((float)fraccion);
            return;
        }

        EscalaProgreso.ScaleX = fraccion;
        if (_musica?.Progreso() is var (_, duracion))
            TiempoMusica.Text = $"{Formatear(duracion * fraccion)} / {Formatear(duracion)}";
        if (soltando && _musica is not null) _ = _musica.SaltarAsync(fraccion);
    }

    // Arrastrar solo en reposo: expandida, el clic es para los botones o para cerrar el aviso
    private void Zona_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_modo != Modo.Reposo) return;

        _entrada.Stop();   // que no se abra la musica mientras se arrastra
        DragMove();
        Reorientar();
    }

    // ---------- Reposo ----------

    /// <summary>
    /// En reposo la pildora muestra la musica si suena (portada + hora + barritas) y el temporizador si corre;
    /// con los dos, la cuenta regresiva ocupa el lugar de la hora, en el color del temporizador.
    /// </summary>
    private bool ReposoConContenido => _temporizador?.Activo == true || _musica?.Estado?.Reproduciendo == true;

    // Con musica es mas ancha porque lleva la hora (o la cuenta regresiva) en el medio
    /// <summary>
    /// Musica o temporizador agrandan la pildora; los indicadores (camara, microfono, Bloq Mayus) le suman lugar a la
    /// derecha. Con solo indicadores queda una pildora chica con los puntitos.
    /// </summary>
    private (double Ancho, double Alto) TamañoReposo
    {
        get
        {
            var (ancho, alto) = _musica?.Estado?.Reproduciendo == true ? (156.0, 28.0)
                              : _temporizador?.Activo == true ? (128.0, 28.0)
                              : (96.0, 10.0);
            int indicadores = CantidadIndicadores;
            if (indicadores == 0) return (ancho, alto);
            return alto < 20 ? (24 + AnchoIndicador * indicadores, 22) : (ancho + AnchoIndicador * indicadores, alto);
        }
    }

    private void ActualizarReposo()
    {
        bool temporizador = _temporizador?.Activo == true;
        if (_musica?.Estado is { Reproduciendo: true } estado)
        {
            MiniPortada.Background = estado.Portada is null
                ? new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A))
                : new ImageBrush(estado.Portada) { Stretch = Stretch.UniformToFill };
            IconoReposo.Text = estado.Portada is null ? "" : "";
            IconoReposo.Foreground = Brushes.White;
            TiempoReposo.Visibility = Visibility.Collapsed;
            RelojReposo.Text = temporizador ? Formatear(_temporizador!.Restante) : DateTime.Now.ToString("HH:mm");
            RelojReposo.Foreground = temporizador ? new SolidColorBrush(_temporizador!.Color) : Brushes.White;
            RelojReposo.Visibility = Visibility.Visible;
            Ecualizador.Visibility = Visibility.Visible;
            Ecualizar(true);
        }
        else if (temporizador)
        {
            MiniPortada.Background = null;
            IconoReposo.Text = "";
            IconoReposo.Foreground = new SolidColorBrush(_temporizador!.Color);
            TiempoReposo.Text = Formatear(_temporizador.Restante);
            TiempoReposo.Visibility = Visibility.Visible;
            RelojReposo.Visibility = Visibility.Collapsed;
            Ecualizador.Visibility = Visibility.Collapsed;
            Ecualizar(false);
        }
        else
        {
            Ecualizar(false);
        }

        if (_modo != Modo.Reposo) return;

        if (TamañoReposo != _reposoDestino)
        {
            _ajusteReposo.Stop();   // cambia el tamaño del reposo: se aplica cuando el estado se quede quieto
            _ajusteReposo.Start();
        }
        else if (ReposoConContenido && ContenidoReposo.Visibility != Visibility.Visible) MostrarContenidoReposo();
    }

    // ---------- Indicadores de lo que esta en uso ----------

    private const double AnchoIndicador = 13;
    private AvisosSistema? _avisosSistema;

    public void ConectarIndicadores(AvisosSistema avisos)
    {
        _avisosSistema = avisos;
        avisos.EnUsoCambio += AlCambiarEnUso;
    }

    private bool IndicadoresPrendidos => !Demo.Activo && !_config.AvisosApagados.Contains(TiposAviso.Indicadores);
    private bool CamaraEnUso => IndicadoresPrendidos && _avisosSistema?.AppsCamara.Count > 0;
    private bool MicrofonoEnUso => IndicadoresPrendidos && _avisosSistema?.AppsMicrofono.Count > 0;
    private bool BloqMayusEnUso => IndicadoresPrendidos && _avisosSistema?.BloqMayus == true;
    private int CantidadIndicadores => (CamaraEnUso ? 1 : 0) + (MicrofonoEnUso ? 1 : 0) + (BloqMayusEnUso ? 1 : 0);

    private void AlCambiarEnUso(string? empezo)
    {
        if (empezo is not null && IndicadoresPrendidos) Destellar(empezo);
        ActualizarIndicadores();
        if (_modo is Modo.Inicio or Modo.Musica) Expandir(_modo);   // la linea "En uso" cambia el alto
        ActualizarReposo();   // si esta cerrada, ajusta el tamaño
    }

    /// <summary>Los puntitos del reposo y el margen del contenido (musica o temporizador) para dejarles lugar.</summary>
    private void ActualizarIndicadores()
    {
        PuntoCamara.Visibility = CamaraEnUso ? Visibility.Visible : Visibility.Collapsed;
        PuntoMicrofono.Visibility = MicrofonoEnUso ? Visibility.Visible : Visibility.Collapsed;
        PuntoBloqMayus.Visibility = BloqMayusEnUso ? Visibility.Visible : Visibility.Collapsed;
        int indicadores = CantidadIndicadores;
        bool conContenido = ReposoConContenido;
        IndicadoresReposo.HorizontalAlignment = conContenido ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        IndicadoresReposo.Margin = new Thickness(0, 0, conContenido ? 9 : 0, 0);
        ContenidoReposo.Margin = new Thickness(5, 0, 10 + (conContenido ? AnchoIndicador * indicadores : 0), 0);
        IndicadoresReposo.Visibility = _modo == Modo.Reposo && indicadores > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Dos destellos del borde, del color de lo que empezo. Corto: no queda nada animandose.</summary>
    private void Destellar(string que)
    {
        Destello.BorderBrush = new SolidColorBrush(que switch
        {
            "webcam" => Color.FromRgb(0x30, 0xD1, 0x58),
            "microphone" => Color.FromRgb(0xFF, 0x9F, 0x0A),
            _ => Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF),
        });
        var animacion = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1300) };
        foreach (var (ms, valor) in new[] { (0, 0.0), (180, 1.0), (450, 0.25), (650, 1.0), (1300, 0.0) })
            animacion.KeyFrames.Add(new EasingDoubleKeyFrame(valor, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new QuadraticEase()));
        Timeline.SetDesiredFrameRate(animacion, 30);
        Destello.BeginAnimation(OpacityProperty, animacion);
    }

    /// <summary>Hay linea "En uso" en este panel (el primero de la ronda: Inicio, o Musica si suena algo).</summary>
    private bool ConEnUso(Modo modo) => modo is Modo.Inicio or Modo.Musica && CantidadIndicadores > 0;

    /// <summary>
    /// La linea "En uso" abajo del primer panel: que app usa la camara, cual el microfono, y Bloq Mayus.
    /// El contenido del panel se corre hacia arriba para dejarle lugar.
    /// </summary>
    private void MostrarEnUso(Modo modo)
    {
        bool visible = ConEnUso(modo);
        ContenidoInicio.Margin = new Thickness(22, 0, 12, visible ? 24 : 0);
        ContenidoMusica.Margin = new Thickness(14, 0, 12, visible ? 24 : 0);
        EnUso.Children.Clear();
        EnUso.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        void Agregar(UIElement marca, string texto)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 0) };
            item.Children.Add(marca);
            item.Children.Add(new TextBlock { Text = texto, Foreground = new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)), FontSize = 11.5,
                                              Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            EnUso.Children.Add(item);
        }
        Ellipse Punto(Color color) => new() { Width = 7, Height = 7, Fill = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center };

        if (CamaraEnUso) Agregar(Punto(Color.FromRgb(0x30, 0xD1, 0x58)), Idioma.T("Inicio.Camara", string.Join(", ", _avisosSistema!.AppsCamara)));
        if (MicrofonoEnUso) Agregar(Punto(Color.FromRgb(0xFF, 0x9F, 0x0A)), Idioma.T("Inicio.Microfono", string.Join(", ", _avisosSistema!.AppsMicrofono)));
        if (BloqMayusEnUso)
            Agregar(new Path { Data = (Geometry)FindResource("FormaBloqMayus"), Fill = Brushes.White, Stretch = Stretch.Uniform,
                               Width = 9, Height = 11, VerticalAlignment = VerticalAlignment.Center }, Idioma.T("Inicio.BloqMayus"));
    }

    private void MostrarContenidoReposo()
    {
        ContenidoReposo.Visibility = Visibility.Visible;
        Animar(ContenidoReposo, OpacityProperty, 1, 200, null);
    }

    /// <summary>Barritas que suben y bajan mientras suena algo (decorativas, no siguen el audio real).</summary>
    private void Ecualizar(bool activo)
    {
        double[] periodos = [0.45, 0.62, 0.5, 0.7];
        int i = 0;
        foreach (Rectangle barra in Ecualizador.Children)
        {
            var escala = (ScaleTransform)barra.RenderTransform;
            if (activo)
            {
                if (escala.HasAnimatedProperties) { i++; continue; }
                var animacion = new DoubleAnimation(0.25, 1, TimeSpan.FromSeconds(periodos[i++]))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                // Corre todo el tiempo que suena musica y la ventana transparente se redibuja entera por CPU en
                // cada cuadro: a 60 fps eran ~10% de un nucleo. A 20 se ve igual de fluido para unas barritas.
                Timeline.SetDesiredFrameRate(animacion, 20);
                escala.BeginAnimation(ScaleTransform.ScaleYProperty, animacion);
            }
            else
            {
                escala.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                escala.ScaleY = 0.4;
            }
        }
    }

    // ---------- Animaciones ----------

    private (FrameworkElement Contenido, double Ancho, double Alto) Forma(Modo modo) => modo switch
    {
        Modo.Aviso => (ContenidoAviso, 380, 64),
        Modo.Musica => (ContenidoMusica, 440, ConEnUso(modo) ? 116 : 92),
        Modo.Herramientas => (ContenidoHerramientas, 440, 92),
        Modo.Opciones => (ContenidoOpciones, 440, 376),
        Modo.Calendario => (ContenidoCalendario, 440, AltoCalendario()),
        Modo.Clima => (ContenidoClima, 440, 92),
        Modo.Pc => (ContenidoPc, 440, 134),
        Modo.Notas => (ContenidoNotas, 420, 126),
        Modo.Inicio => (ContenidoInicio, 452, ConEnUso(modo) ? 116 : 92),
        Modo.Tutorial => (ContenidoTutorial, 440, 250),
        Modo.Calendarios => (ContenidoCalendarios, 440, Math.Min(250, 116 + 28 * _config.Calendarios.Count)),
        Modo.Avisos => (ContenidoAvisos, 440, 90 + 32 * ((TiposAviso.Todos.Length + 1) / 2)),
        Modo.Bienvenida => (ContenidoBienvenida, 440, 236),
        _ => (ContenidoVolumen, 300, 40),
    };

    private FrameworkElement[] Contenidos =>
        [ContenidoReposo, ContenidoAviso, ContenidoMusica, ContenidoVolumen, ContenidoHerramientas, ContenidoOpciones,
         ContenidoCalendario, ContenidoClima, ContenidoPc, ContenidoNotas, ContenidoTutorial, ContenidoCalendarios, ContenidoInicio, ContenidoAvisos,
         ContenidoBienvenida];

    /// <param name="deslizarDesde">Si no es 0, el contenido nuevo entra deslizandose desde ese desplazamiento.</param>
    private void Expandir(Modo modo, double deslizarDesde = 0)
    {
        if (_conVideo && !IsVisible)
        {
            Show();   // ShowActivated=False: no le saca el foco al video
            Nativo.ReafirmarTopmost(_hwnd);
        }
        if (modo != Modo.Volumen) SoltarZona();
        IndicadoresReposo.Visibility = Visibility.Collapsed;
        MostrarEnUso(modo);
        if (_modo == Modo.Bienvenida && modo != Modo.Bienvenida) AnimarDemo(null);
        if (modo == Modo.Bienvenida) MostrarPasoBienvenida();
        if (modo == Modo.Herramientas)
        {
            BotonCancelarTemporizador.Visibility = _temporizador?.Activo == true ? Visibility.Visible : Visibility.Collapsed;
            if (_modo != Modo.Herramientas) MostrarBotonesAccesos();   // al volver a abrir, la fila de botones
        }
        if (modo == Modo.Opciones)
        {
            InterruptorArranque.IsChecked = Arranque.Activo;   // el registro es la fuente de verdad
            InterruptorFoco.IsChecked = _config.FocoPomodoro;
            InterruptorNotificaciones.IsChecked = _config.NotificacionesWindows;
            InterruptorNoMolestar.IsChecked = _config.RespetarNoMolestar;
            InterruptorNoMolestar.IsEnabled = _config.NotificacionesWindows;
            InterruptorCalendario.IsChecked = _config.PanelCalendario;
            InterruptorClima.IsChecked = _config.PanelClima;
            InterruptorPc.IsChecked = _config.PanelPc;
            InterruptorNotas.IsChecked = _config.PanelNotas;
            ResumirCalendarios();
        }
        if (modo == Modo.Calendarios)
        {
            _eligiendoColor = -1;
            MostrarCalendarios();
        }
        if (modo == Modo.Avisos)
        {
            foreach (var interruptor in _interruptoresAvisos)
                if (interruptor.Tag is string tipo) interruptor.IsChecked = !_config.AvisosApagados.Contains(tipo);
        }
        if (modo == Modo.Tutorial && PasosTutorial.Children.Count == 0)
        {
            // Por si se llega sin pasar por Tutorial_Click (ej. --panel tutorial): arranca en Google
            TutorialGoogle.IsChecked = true;
            _tutorialProveedor = "google";
            MostrarPasos();
        }
        if (_escribiendo && modo is not (Modo.Notas or Modo.Opciones or Modo.Bienvenida)) SoltarTeclado();
        if (modo == Modo.Calendario)
        {
            if (_modo == Modo.Reposo) VolverAHoy();   // al abrir la isla de cero, el calendario arranca en hoy
            else MostrarCalendario();
        }
        if (modo == Modo.Clima) MostrarClima();
        if (modo == Modo.Inicio) MostrarInicio();
        if (modo == Modo.Pc) { _ = LeerPcAsync(); _lecturaPc.Start(); }
        if (modo != Modo.Pc && !_limpiando) ReiniciarLimpieza();   // salir del panel cancela una confirmacion pendiente
        else _lecturaPc.Stop();
        ActualizarPuntos(modo);
        if (modo is Modo.Aviso or Modo.Volumen or Modo.Bienvenida || !Paginas.Contains(modo)) OcultarFlechas();

        // Pantallas con flecha de volver: se recuerda desde que panel se llego (no al volver, para no hacer un ciclo)
        if (modo is Modo.Opciones or Modo.Calendarios or Modo.Tutorial or Modo.Avisos && modo != _modo && EnPanel && !_volviendo)
            _volverA[modo] = _modo;
        _volviendo = false;

        _modo = modo;
        AplicarCapa();   // "detras de las apps": al mostrar algo pasa al frente
        _reposoDestino = (-1, -1);   // ya no esta en reposo
        _entrada.Stop();
        _salida.Stop();
        _ajusteReposo.Stop();
        if (modo != Modo.Volumen) _cierreVolumen.Stop();
        if (modo != Modo.Musica) _cierreTemaNuevo.Stop();

        var (mostrar, ancho, alto) = Forma(modo);
        foreach (var contenido in Contenidos)
        {
            if (contenido == mostrar) continue;
            contenido.BeginAnimation(OpacityProperty, null);
            contenido.Opacity = 0;
            contenido.Visibility = Visibility.Collapsed;
        }
        mostrar.Visibility = Visibility.Visible;

        if (modo == Modo.Musica) { ActualizarProgreso(); _progreso.Start(); }
        else _progreso.Stop();

        var rebote = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
        Animar(Pildora, WidthProperty, ancho, 480, rebote);
        Animar(Pildora, HeightProperty, alto, 480, rebote);
        if (deslizarDesde != 0 && mostrar.RenderTransform is TranslateTransform desplazamiento)
        {
            desplazamiento.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(deslizarDesde, 0, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
            Animar(mostrar, OpacityProperty, 1, 220, null);
        }
        else
        {
            Animar(mostrar, OpacityProperty, 1, 250, null, demora: 180);
        }
    }

    /// <summary>La zona vuelve a medir lo mismo que la pildora (ver el volumen abierto desde un panel).</summary>
    private void SoltarZona()
    {
        Zona.MinWidth = 0;
        Zona.MinHeight = 0;
    }

    private void Colapsar()
    {
        _cierre.Stop();
        _cierreVolumen.Stop();
        _cierreTemaNuevo.Stop();
        _progreso.Stop();
        _ajusteReposo.Stop();
        _lecturaPc.Stop();
        if (_escribiendo) SoltarTeclado();
        SoltarZona();
        Pildora.Cursor = null;
        OcultarFlechas();
        EnUso.Visibility = Visibility.Collapsed;
        if (_modo == Modo.Bienvenida) AnimarDemo(null);
        _modo = Modo.Reposo;
        AplicarCapa();   // "detras de las apps": vuelve atras
        Puntos.Visibility = Visibility.Collapsed;

        var destino = TamañoReposo;
        _reposoDestino = destino;
        var (ancho, alto) = destino;
        var suave = new CubicEase { EasingMode = EasingMode.EaseInOut };
        foreach (var contenido in Contenidos)
            Animar(contenido, OpacityProperty, 0, 150, null);
        Animar(Pildora, WidthProperty, ancho, 380, suave, demora: 100);
        Animar(Pildora, HeightProperty, alto, 380, suave, demora: 100, alTerminar: () =>
        {
            if (_modo != Modo.Reposo || _reposoDestino != destino) return;   // otra animacion tomo el control
            foreach (var contenido in Contenidos)
                if (contenido != ContenidoReposo) contenido.Visibility = Visibility.Collapsed;

            if (TamañoReposo != destino)
            {
                Colapsar();   // el estado cambio durante la animacion: ir al tamaño correcto
                return;
            }
            if (ReposoConContenido) MostrarContenidoReposo();
            else ContenidoReposo.Visibility = Visibility.Collapsed;
            ActualizarIndicadores();
            MostrarSiguienteDeLaCola();
            // Termino el aviso sobre el video o el juego: vuelve a no tapar
            if ((_conVideo || _ocultaPorPantallaCompleta) && _modo == Modo.Reposo) Hide();
        });
    }

    /// <summary>Un puntito por panel de la ronda; el actual resaltado. Solo si hay mas de uno.</summary>
    private void ActualizarPuntos(Modo modo)
    {
        if (modo == Modo.Bienvenida)
        {
            ArmarPuntos(PasosBienvenida.Length, (int)_pasoBienvenida, soloAtras: true);
            return;
        }
        var paginas = Paginas;
        if (paginas.Length < 2 || Array.IndexOf(paginas, modo) < 0)
        {
            Puntos.Children.Clear();
            Puntos.Visibility = Visibility.Collapsed;
            return;
        }
        ArmarPuntos(paginas.Length, Array.IndexOf(paginas, modo), soloAtras: false);
    }

    /// <summary>
    /// Cada puntito va dentro de un area de clic mas grande (los puntos miden 5): clic = ir a ese panel.
    /// En la bienvenida solo se puede volver a pasos anteriores (los de adelante pueden esperar una accion).
    /// </summary>
    private void ArmarPuntos(int cantidad, int actual, bool soloAtras)
    {
        Puntos.Children.Clear();
        for (int i = 0; i < cantidad; i++)
        {
            bool clickeable = i != actual && (!soloAtras || i < actual);
            var punto = new Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = new SolidColorBrush(i == actual ? Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF)),
            };
            var area = new Border
            {
                Width = 12,
                Height = 13,
                Background = Brushes.Transparent,
                Child = punto,
                Tag = i,
                Cursor = clickeable ? Cursors.Hand : null,
                IsHitTestVisible = clickeable,
            };
            area.MouseEnter += (_, _) => punto.Opacity = 0.7;
            area.MouseLeave += (_, _) => punto.Opacity = 1;
            area.MouseLeftButtonDown += Punto_MouseLeftButtonDown;
            Puntos.Children.Add(area);
        }
        Puntos.Visibility = Visibility.Visible;
    }

    private void Punto_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int indice }) return;
        e.Handled = true;
        if (_modo == Modo.Bienvenida)
        {
            if (indice >= (int)_pasoBienvenida) return;
            _pasoBienvenida = (PasoBienvenida)indice;
            Expandir(Modo.Bienvenida, deslizarDesde: -40);
            return;
        }
        var paginas = Paginas;
        int actual = Array.IndexOf(paginas, _modo);
        if (actual < 0 || indice == actual || indice >= paginas.Length) return;
        Expandir(paginas[indice], deslizarDesde: indice > actual ? 40 : -40);
    }

    // ---------- Flechas a los costados ----------

    private const double ZonaFlecha = 56;   // a cuantos px del borde aparece la flecha de ese lado

    /// <summary>Las flechas aparecen al acercar el mouse a un costado, solo en los paneles de la ronda.</summary>
    private void Zona_MouseMove(object sender, MouseEventArgs e)
    {
        bool enRonda = _inicioGesto is null && _pistaArrastrada is null && Paginas is { Length: > 1 } paginas && Array.IndexOf(paginas, _modo) >= 0;
        double x = e.GetPosition(Pildora).X;
        MostrarFlecha(FlechaIzquierda, enRonda && x < ZonaFlecha);
        MostrarFlecha(FlechaDerecha, enRonda && x > Pildora.ActualWidth - ZonaFlecha);
    }

    private void OcultarFlechas()
    {
        MostrarFlecha(FlechaIzquierda, false);
        MostrarFlecha(FlechaDerecha, false);
    }

    private static void MostrarFlecha(Border flecha, bool visible)
    {
        if (flecha.IsHitTestVisible == visible) return;
        flecha.IsHitTestVisible = visible;
        flecha.BeginAnimation(OpacityProperty, new DoubleAnimation(visible ? 1 : 0, TimeSpan.FromMilliseconds(visible ? 150 : 120)));
    }

    private void Flecha_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string direccion }) return;
        e.Handled = true;
        Pasar(int.Parse(direccion));
    }

    private static void Animar(UIElement elemento, DependencyProperty propiedad, double hasta,
                               int ms, IEasingFunction? curva, int demora = 0, Action? alTerminar = null)
    {
        var animacion = new DoubleAnimation(hasta, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = curva,
            BeginTime = TimeSpan.FromMilliseconds(demora),
        };
        // Completed hay que engancharlo ANTES de BeginAnimation: despues ya no se dispara
        if (alTerminar is not null) animacion.Completed += (_, _) => alTerminar();
        elemento.BeginAnimation(propiedad, animacion);
    }

    // ---------- Menu y clics ----------

    private void Pildora_Click(object sender, MouseButtonEventArgs e)
    {
        if (_modo != Modo.Aviso) return;
        var accion = _alTocarAviso;
        _alTocarAviso = null;
        Colapsar();
        accion?.Invoke();
    }

    // Botones del panel de accesos. Tag: "25+5" = Pomodoro (estudio + descanso), "10" = temporizador simple, en minutos.
    private void Temporizador_Click(object sender, RoutedEventArgs e)
    {
        if (_temporizador is null || sender is not FrameworkElement { Tag: string tag }) return;

        var partes = tag.Split('+').Select(p => TimeSpan.FromMinutes(int.Parse(p))).ToArray();
        if (partes.Length == 2) _temporizador.IniciarPomodoro(partes[0], partes[1]);
        else _temporizador.Iniciar(partes[0]);
        if (_modo == Modo.Herramientas) Colapsar();   // que se vea la cuenta regresiva en la pildora
    }

    // ---------- Temporizador personalizado ----------

    private const int PasoMinutos = 5, MinimoMinutos = 5, MaximoMinutos = 180;

    private void Personalizado_Click(object sender, RoutedEventArgs e)
    {
        BotonesAccesos.Visibility = Visibility.Collapsed;
        SelectorPersonalizado.Visibility = Visibility.Visible;
        MostrarMinutosPersonalizados();
    }

    private void VolverAccesos_Click(object sender, RoutedEventArgs e) => MostrarBotonesAccesos();

    private void MostrarBotonesAccesos()
    {
        SelectorPersonalizado.Visibility = Visibility.Collapsed;
        BotonesAccesos.Visibility = Visibility.Visible;
    }

    private void MenosMinutos_Click(object sender, RoutedEventArgs e) => CambiarMinutos(-PasoMinutos);
    private void MasMinutos_Click(object sender, RoutedEventArgs e) => CambiarMinutos(PasoMinutos);

    private void SelectorPersonalizado_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;   // aca la rueda elige los minutos, no el volumen
        CambiarMinutos(Math.Sign(e.Delta) * PasoMinutos);
    }

    private void CambiarMinutos(int delta)
    {
        int minutos = Math.Clamp(_config.TemporizadorPersonalizado + delta, MinimoMinutos, MaximoMinutos);
        if (minutos == _config.TemporizadorPersonalizado) return;
        _config.TemporizadorPersonalizado = minutos;
        _config.Guardar();
        MostrarMinutosPersonalizados();
    }

    private void MostrarMinutosPersonalizados() =>
        MinutosPersonalizado.Text = Idioma.T("Herr.Minutos", _config.TemporizadorPersonalizado);

    private void IniciarPersonalizado_Click(object sender, RoutedEventArgs e)
    {
        _temporizador?.Iniciar(TimeSpan.FromMinutes(_config.TemporizadorPersonalizado));
        MostrarBotonesAccesos();
        Colapsar();   // que se vea la cuenta regresiva en la pildora
    }

    // ---------- Modo foco (Pomodoro) ----------

    private void Foco_Click(object sender, RoutedEventArgs e)
    {
        _config.FocoPomodoro = InterruptorFoco.IsChecked == true;
        _config.Guardar();
        if (!_config.FocoPomodoro) LiberarRetenidos();
    }

    /// <summary>Al terminar el bloque de estudio (o cancelarlo) los avisos retenidos pasan a la cola normal.</summary>
    private void LiberarRetenidos()
    {
        if (_retenidos.Count == 0 || (_config.FocoPomodoro && _temporizador?.Estudiando == true)) return;
        Log.Escribir($"foco: se liberan {_retenidos.Count} avisos retenidos");
        foreach (var aviso in _retenidos)
            if (_cola.Count < MaxCola) _cola.Enqueue(aviso);
        _retenidos.Clear();
        MostrarSiguienteDeLaCola();
    }

    private void CancelarTemporizador_Click(object sender, RoutedEventArgs e)
    {
        _temporizador?.Cancelar();
        if (_modo == Modo.Herramientas) Colapsar();
    }

    private void VolverAlCentro_Click(object sender, RoutedEventArgs e)
    {
        Posicion.Borrar();
        Ubicar();
        if (_modo == Modo.Herramientas) Colapsar();
    }

    private void Salir_Click(object sender, RoutedEventArgs e) => Close();

    // ---------- Actualizaciones ----------

    private static readonly Color AzulAviso = Color.FromRgb(0x0A, 0x84, 0xFF);

    private async void BuscarActualizaciones_Click(object sender, RoutedEventArgs e) => await BuscarActualizacionAsync(aPedido: true);

    /// <summary>Si hay version nueva, un aviso que al tocarlo la instala. A pedido tambien avisa si ya esta al dia.</summary>
    private async Task BuscarActualizacionAsync(bool aPedido)
    {
        if (_actualizando) return;
        VersionNueva? nueva;
        try
        {
            nueva = await Actualizaciones.BuscarAsync();
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                         or KeyNotFoundException or InvalidOperationException)
        {
            Log.Escribir($"actualizaciones: no se pudo consultar: {ex.GetType().Name}");
            if (aPedido)
                MostrarAviso(new Aviso("\uE774", Idioma.T("Act.SinConexion"), Idioma.T("Act.SinConexionDetalle"), AzulAviso, Carga: null, Persistente: false, Segundos: 5));
            return;
        }

        if (nueva is null)
        {
            if (aPedido)
                MostrarAviso(new Aviso("\uE73E", Idioma.T("Act.AlDia"), $"DeskIsleBar {Actualizaciones.Actual}", Color.FromRgb(0x30, 0xD1, 0x58),
                                       Carga: null, Persistente: false, Segundos: 4));
            return;
        }
        Log.Escribir($"actualizaciones: hay una version nueva ({nueva.Version})");
        MostrarAviso(new Aviso("\uE896", Idioma.T("Act.Nueva", nueva.Version), Idioma.T("Act.NuevaDetalle"), AzulAviso,
                               Carga: null, Persistente: false, Segundos: 12, AlTocar: () => _ = InstalarActualizacionAsync(nueva)));
    }

    private async Task InstalarActualizacionAsync(VersionNueva nueva)
    {
        if (_actualizando) return;
        _actualizando = true;
        MostrarAviso(new Aviso("\uE896", Idioma.T("Act.Descargando"), $"DeskIsleBar {nueva.Version}", AzulAviso, Carga: null, Persistente: false, Segundos: 60));
        if (await Actualizaciones.InstalarAsync(nueva))
        {
            Application.Current.Shutdown();   // el instalador espera a que se cierre y la vuelve a abrir
            return;
        }
        _actualizando = false;
        if (_modo == Modo.Aviso) Colapsar();
        MostrarAviso(new Aviso("\uE7BA", Idioma.T("Act.Error"), Idioma.T("Act.ErrorDetalle"), Color.FromRgb(0xFF, 0x9F, 0x0A),
                               Carga: null, Persistente: false, Segundos: 8));
    }

    /// <summary>Herramienta de desarrollo: "--panel calendario" abre ese panel al arrancar (para capturas).</summary>
    public void AbrirPanel(string nombre)
    {
        _bienvenidaPendiente = false;   // se pidio otro panel: la bienvenida no lo tapa
        // "bienvenida.clima": un paso puntual de la bienvenida
        if (nombre.Split('.') is ["bienvenida", var paso] && Enum.TryParse(paso, ignoreCase: true, out PasoBienvenida p))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                MostrarBienvenida();
                _pasoBienvenida = p;
                Expandir(Modo.Bienvenida);
            });
            return;
        }
        if (Enum.TryParse(nombre, ignoreCase: true, out Modo modo) && modo is not (Modo.Reposo or Modo.Aviso or Modo.Volumen))
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => Expandir(modo));
    }

    // ---------- Opciones ----------

    private void Opciones_Click(object sender, RoutedEventArgs e) => Expandir(Modo.Opciones, deslizarDesde: 40);

    private void Avisos_Click(object sender, RoutedEventArgs e) => Expandir(Modo.Avisos, deslizarDesde: 40);

    private readonly List<ToggleButton> _interruptoresAvisos = [];

    /// <summary>Un interruptor por tipo de aviso (<see cref="TiposAviso.Todos"/>), con su texto del diccionario.</summary>
    private void ArmarInterruptoresAvisos()
    {
        foreach (var (tipo, clave) in TiposAviso.Todos)
        {
            var interruptor = new ToggleButton { Style = (Style)FindResource("Interruptor"), Tag = tipo };
            interruptor.Click += InterruptorAviso_Click;
            var texto = new TextBlock { Foreground = Brushes.White, FontSize = 12.5, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            texto.SetResourceReference(TextBlock.TextProperty, clave);
            var fila = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            fila.Children.Add(interruptor);
            fila.Children.Add(texto);
            _interruptoresAvisos.Add(interruptor);
            // Los indicadores van solos en su fila, a todo el ancho: el texto es largo y en media columna se corta
            if (tipo == TiposAviso.Indicadores) ((Panel)InterruptoresAvisos.Parent).Children.Add(fila);
            else InterruptoresAvisos.Children.Add(fila);
        }
    }

    private void InterruptorAviso_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string tipo } interruptor) return;
        if (interruptor.IsChecked == true) _config.AvisosApagados.Remove(tipo);
        else if (!_config.AvisosApagados.Contains(tipo)) _config.AvisosApagados.Add(tipo);
        _config.Guardar();
        if (tipo == TiposAviso.Captura && _notificaciones is not null)
            _notificaciones.OmitirRecortes = interruptor.IsChecked == true;
        if (tipo == TiposAviso.Indicadores) ActualizarIndicadores();
    }

    /// <summary>Flecha de volver de Opciones, Calendarios y Tutorial: al panel desde el que se llego.</summary>
    private void Volver_Click(object sender, RoutedEventArgs e)
    {
        var destino = _volverA.GetValueOrDefault(_modo, _modo == Modo.Tutorial ? Modo.Calendarios : Modo.Herramientas);
        _volviendo = true;
        Expandir(destino, deslizarDesde: -40);   // entra desde la izquierda: se lee como "atras"
    }

    private void Arranque_Click(object sender, RoutedEventArgs e) => Arranque.Fijar(InterruptorArranque.IsChecked == true);

    private void Notificaciones_Click(object sender, RoutedEventArgs e)
    {
        _config.NotificacionesWindows = InterruptorNotificaciones.IsChecked == true;
        _config.RespetarNoMolestar = InterruptorNoMolestar.IsChecked == true;
        _config.Guardar();
        InterruptorNoMolestar.IsEnabled = _config.NotificacionesWindows;
        if (_notificaciones is null) return;
        _notificaciones.Activo = _config.NotificacionesWindows;
        _notificaciones.RespetarNoMolestar = _config.RespetarNoMolestar;
    }

    /// <summary>Un chip por app de <see cref="Musica.Apps"/>; marca la guardada en el config.</summary>
    private void ArmarChipsMusica()
    {
        if (!Musica.Apps.Any(a => a.Clave == _config.AppMusica))
        {
            _config.AppMusica = "auto";   // era una app que ya no esta en la lista (ej. YouTube Music)
            _config.Guardar();
        }
        foreach (var (clave, nombre, _) in Musica.Apps)
        {
            var chip = new RadioButton
            {
                Tag = clave,
                GroupName = "AppMusica",
                Style = (Style)FindResource("Chip"),
                IsChecked = clave == _config.AppMusica,
                // Mas compactos que los del tutorial: los cinco tienen que entrar en una fila
                Padding = new Thickness(7, 4, 7, 4),
                Margin = new Thickness(0, 0, 4, 0),
            };
            // Los que se traducen siguen al diccionario (cambian solos con el idioma); los demas son nombres propios
            if (nombre.StartsWith("Musica.")) chip.SetResourceReference(ContentProperty, nombre);
            else chip.Content = nombre;
            chip.Click += AppMusica_Click;
            ChipsMusica.Children.Add(chip);
        }
    }

    /// <summary>Automático + un chip por idioma disponible (cada uno con su nombre en su propio idioma).</summary>
    private void ArmarChipsIdioma()
    {
        foreach (var (codigo, nombre) in Idioma.Disponibles.Prepend(("auto", "")))
        {
            var chip = new RadioButton
            {
                Tag = codigo,
                GroupName = "Idioma",
                Style = (Style)FindResource("Chip"),
                IsChecked = codigo == _config.Idioma,
                Padding = new Thickness(7, 4, 7, 4),
                Margin = new Thickness(0, 0, 4, 0),
            };
            if (codigo == "auto") chip.SetResourceReference(ContentProperty, "Op.IdiomaAuto");
            else chip.Content = nombre;
            chip.Click += Idioma_Click;
            ChipsIdioma.Children.Add(chip);
        }
    }

    /// <summary>Siempre / Auto / Detrás (ver <see cref="Config.PrimerPlano"/>), con la explicación de cada uno al pasar el mouse.</summary>
    private void ArmarChipsPrimerPlano()
    {
        foreach (var (codigo, clave) in new[] { ("siempre", "Op.CapaSiempre"), ("auto", "Op.CapaAuto"), ("detras", "Op.CapaDetras") })
        {
            var chip = new RadioButton
            {
                Tag = codigo,
                GroupName = "PrimerPlano",
                Style = (Style)FindResource("Chip"),
                IsChecked = codigo == _config.PrimerPlano,
                Padding = new Thickness(7, 4, 7, 4),
                Margin = new Thickness(0, 0, 4, 0),
            };
            chip.SetResourceReference(ContentProperty, clave);
            chip.SetResourceReference(ToolTipProperty, clave + "Detalle");
            chip.Click += PrimerPlano_Click;
            ChipsPrimerPlano.Children.Add(chip);
        }
    }

    private void PrimerPlano_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string codigo } || codigo == _config.PrimerPlano) return;
        _config.PrimerPlano = codigo;
        _config.Guardar();
        Log.Escribir($"primer plano: {codigo}");
        Vigilar();   // se aplica ya, sin esperar al proximo segundo
    }

    private void Idioma_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string codigo } || codigo == _config.Idioma) return;
        _config.Idioma = codigo;
        _config.Guardar();
        Idioma.Aplicar(codigo);
    }

    /// <summary>
    /// El XAML cambia solo (DynamicResource); aca se rehace lo que arma el codigo y esta a la vista o en cache.
    /// Los demas paneles se vuelven a dibujar al abrirlos.
    /// </summary>
    private void AlCambiarIdioma()
    {
        PasosTutorial.Children.Clear();
        if (!_limpiando) ReiniciarLimpieza();
        ActualizarHora();
        if (_modo == Modo.Opciones) ResumirCalendarios();
        Log.Escribir($"idioma: {Idioma.Actual}");
    }

    private void ResumirCalendarios()
    {
        int n = _config.Calendarios.Count;
        ResumenCalendarios.Text = n switch { 0 => Idioma.T("Op.CalNinguno"), 1 => Idioma.T("Op.CalUno"), _ => Idioma.T("Op.CalVarios", n) };
        TextoBotonCalendarios.SetResourceReference(TextBlock.TextProperty, n == 0 ? "Op.VincularCalendario" : "Op.AdministrarCalendarios");
    }

    private void AppMusica_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string clave }) return;
        _config.AppMusica = clave;
        _config.Guardar();
        if (_musica is not null) _musica.Preferida = clave;
    }

    private void Panel_Interruptor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string panel } interruptor) return;
        bool visible = interruptor.IsChecked == true;
        switch (panel)
        {
            case "calendario": _config.PanelCalendario = visible; break;
            case "clima": _config.PanelClima = visible; break;
            case "pc": _config.PanelPc = visible; break;
            case "notas": _config.PanelNotas = visible; break;
        }
        _config.Guardar();
    }

    private void CampoCiudad_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Demo.Activo || CampoCiudad.Text.Trim() == _config.Ciudad) return;
        _config.Ciudad = CampoCiudad.Text.Trim();
        _config.Guardar();
        if (_clima is not null) _clima.Ciudad = _config.Ciudad;
    }

    // ---------- Calendarios vinculados ----------

    // Panel al que se vuelve arrastrando desde la administracion de calendarios (de donde se entro)
    private Modo _origenCalendarios = Modo.Opciones;

    private void AdministrarCalendarios_Click(object sender, RoutedEventArgs e)
    {
        _origenCalendarios = Modo.Opciones;
        Expandir(Modo.Calendarios, deslizarDesde: 40);
    }

    private void ConfigurarCalendariosDesdePanel_Click(object sender, RoutedEventArgs e)
    {
        _origenCalendarios = Modo.Calendario;
        Expandir(Modo.Calendarios, deslizarDesde: 40);
    }

    /// <summary>Los links se descifran solo aca y viajan al modulo; nunca se muestran ni se loguean completos.</summary>
    private List<FuenteCalendario> FuentesDesdeConfig()
    {
        var fuentes = new List<FuenteCalendario>();
        foreach (var calendario in _config.Calendarios)
        {
            if (Secreto.Descifrar(calendario.LinkCifrado) is { } url)
                fuentes.Add(new FuenteCalendario(calendario.Nombre, url, ColorDe(calendario.Color)));
            else
                Log.Escribir($"calendario «{calendario.Nombre}»: no se pudo descifrar el link (¿config de otro usuario o PC?)");
        }
        return fuentes;
    }

    private static Color ColorDe(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { return Color.FromRgb(0xFF, 0x9F, 0x0A); }
    }

    private void MostrarCalendarios()
    {
        ListaCalendarios.Children.Clear();
        var conError = _calendario?.ConError ?? [];

        for (int i = 0; i < _config.Calendarios.Count; i++)
        {
            var calendario = _config.Calendarios[i];
            string? url = Secreto.Descifrar(calendario.LinkCifrado);

            var fila = new Grid { Margin = new Thickness(0, 0, 0, 6), Height = 22 };
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fila.ColumnDefinitions.Add(new ColumnDefinition());
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Color: clic en el puntito → en la misma fila aparecen los colores para elegir
            var punto = new Button
            {
                Style = (Style)FindResource("BotonLink"),
                Tag = i,
                ToolTip = Idioma.T("Cals.CambiarColor"),
                Content = new Ellipse { Width = 11, Height = 11, Fill = new SolidColorBrush(ColorDe(calendario.Color)), Margin = new Thickness(0, 0, 2, 0) },
                VerticalAlignment = VerticalAlignment.Center,
            };
            punto.Click += ElegirColor_Click;
            fila.Children.Add(punto);

            // Nombre: parece una etiqueta, se edita al hacer clic (Enter o clic afuera guarda, Esc cancela)
            var nombre = new TextBox
            {
                Text = calendario.Nombre,
                Tag = i,
                Style = (Style)FindResource("CampoEnLinea"),
                MaxLength = 24,
                MinWidth = 40,
                MaxWidth = 230,   // si no entra todo, se achica el link oculto (que trunca con "…"), no el nombre
                Margin = new Thickness(5, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Idioma.T("Cals.Renombrar"),
            };
            nombre.LostKeyboardFocus += NombreCalendario_LostKeyboardFocus;
            Grid.SetColumn(nombre, 1);
            fila.Children.Add(nombre);

            if (i == _eligiendoColor)
            {
                var colores = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                foreach (string hex in Config.PaletaCalendarios)
                {
                    var muestra = new Button
                    {
                        Style = (Style)FindResource("BotonLink"),
                        Tag = (i, hex),
                        Margin = new Thickness(0, 0, 6, 0),
                        Content = new Ellipse
                        {
                            Width = 15,
                            Height = 15,
                            Fill = new SolidColorBrush(ColorDe(hex)),
                            Stroke = hex == calendario.Color ? Brushes.White : null,
                            StrokeThickness = 2,
                        },
                    };
                    muestra.Click += ColorElegido_Click;
                    colores.Children.Add(muestra);
                }
                Grid.SetColumn(colores, 2);
                fila.Children.Add(colores);
                ListaCalendarios.Children.Add(fila);
                continue;   // mientras se elige color, la fila no muestra el link ni el boton de quitar
            }

            bool fallo = url is null || conError.Contains(calendario.Nombre);
            var detalle = new TextBlock
            {
                Text = url is null ? Idioma.T("Cals.NoSePuedeLeer") : fallo ? Idioma.T("Cals.Fallo") : Secreto.Ocultar(url),
                Foreground = fallo ? new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)) : new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(detalle, 2);
            fila.Children.Add(detalle);

            var quitar = new Button
            {
                Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 11 },
                Style = (Style)FindResource("BotonTexto"),
                Tag = i,
                ToolTip = Idioma.T("Cals.Quitar"),
                Margin = new Thickness(8, 0, 0, 0),
            };
            quitar.Click += QuitarCalendario_Click;
            Grid.SetColumn(quitar, 3);
            fila.Children.Add(quitar);

            ListaCalendarios.Children.Add(fila);
        }

        if (_config.Calendarios.Count == 0)
            ListaCalendarios.Children.Add(new TextBlock { Text = Idioma.T("Cals.Ninguno"), Style = (Style)FindResource("Etiqueta"), Margin = new Thickness(0, 0, 0, 8) });

        FilaAgregarCalendario.Visibility = _config.Calendarios.Count < Config.MaxCalendarios ? Visibility.Visible : Visibility.Collapsed;
    }

    private int _eligiendoColor = -1;   // fila que muestra la paleta de colores (-1 = ninguna)

    private void ElegirColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int indice }) return;
        _eligiendoColor = _eligiendoColor == indice ? -1 : indice;   // segundo clic en el puntito: cierra la paleta
        MostrarCalendarios();
    }

    private void ColorElegido_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: (int indice, string hex) } || indice >= _config.Calendarios.Count) return;
        _config.Calendarios[indice].Color = hex;
        _config.Guardar();
        if (_calendario is not null) _calendario.Fuentes = FuentesDesdeConfig();
        _eligiendoColor = -1;
        MostrarCalendarios();
    }

    private void NombreCalendario_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox { Tag: int indice } campo || indice >= _config.Calendarios.Count) return;
        string nombre = campo.Text.Trim();
        if (nombre.Length == 0 || nombre == _config.Calendarios[indice].Nombre)
        {
            campo.Text = _config.Calendarios[indice].Nombre;   // vacio o sin cambios: queda el anterior
            return;
        }
        _config.Calendarios[indice].Nombre = nombre;
        _config.Guardar();
        if (_calendario is not null) _calendario.Fuentes = FuentesDesdeConfig();
    }

    private void AgregarCalendario_Click(object sender, RoutedEventArgs e) => AgregarCalendario();

    private void AgregarCalendario()
    {
        string link = CampoLinkCalendario.Text.Trim();
        bool valido = Uri.TryCreate(link.Replace("webcal://", "https://", StringComparison.OrdinalIgnoreCase), UriKind.Absolute, out var uri)
                      && uri.Scheme == Uri.UriSchemeHttps;
        if (!valido)
        {
            MostrarErrorCalendario(Idioma.T("Cals.LinkInvalido"));
            return;
        }
        if (_config.Calendarios.Count >= Config.MaxCalendarios) return;

        string nombre = CampoNombreCalendario.Text.Trim();
        if (nombre.Length == 0) nombre = Idioma.T("Cals.NombrePorDefecto", _config.Calendarios.Count + 1);
        var usados = _config.Calendarios.Select(c => c.Color).ToHashSet();
        string color = Config.PaletaCalendarios.FirstOrDefault(c => !usados.Contains(c)) ?? Config.PaletaCalendarios[0];

        _config.Calendarios.Add(new CalendarioVinculado { Nombre = nombre, LinkCifrado = Secreto.Cifrar(link), Color = color });
        _config.Guardar();
        if (_calendario is not null) _calendario.Fuentes = FuentesDesdeConfig();

        CampoNombreCalendario.Text = "";
        CampoLinkCalendario.Text = "";   // el link no queda a la vista
        Keyboard.ClearFocus();
        Expandir(Modo.Calendarios);   // re-arma la lista y ajusta el alto
    }

    private void QuitarCalendario_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int indice } || indice >= _config.Calendarios.Count) return;
        _config.Calendarios.RemoveAt(indice);
        _config.Guardar();
        if (_calendario is not null) _calendario.Fuentes = FuentesDesdeConfig();
        Expandir(Modo.Calendarios);
    }

    private void CampoCalendario_TextChanged(object sender, TextChangedEventArgs e)
    {
        PistaNombreCalendario.Visibility = CampoNombreCalendario.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        PistaLinkCalendario.Visibility = CampoLinkCalendario.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ErrorCalendario.Visibility = Visibility.Collapsed;
    }

    private void MostrarErrorCalendario(string mensaje)
    {
        ErrorCalendario.Text = mensaje;
        ErrorCalendario.Visibility = Visibility.Visible;
    }

    // ---------- Teclado (notas y opciones) ----------

    private void TomarTeclado()
    {
        if (_escribiendo) return;
        _escribiendo = true;
        _salida.Stop();
        int estilo = Nativo.GetWindowLong(_hwnd, Nativo.GWL_EXSTYLE);
        Nativo.SetWindowLong(_hwnd, Nativo.GWL_EXSTYLE, estilo & ~Nativo.WS_EX_NOACTIVATE);
        Activate();
    }

    private void SoltarTeclado()
    {
        if (!_escribiendo) return;
        _escribiendo = false;
        Keyboard.ClearFocus();   // dispara LostKeyboardFocus: se guardan ciudad / link .ics
        if (_guardarNotas.IsEnabled)
        {
            _guardarNotas.Stop();
            Notas.Guardar(CampoNotas.Text);
        }
        int estilo = Nativo.GetWindowLong(_hwnd, Nativo.GWL_EXSTYLE);
        Nativo.SetWindowLong(_hwnd, Nativo.GWL_EXSTYLE, estilo | Nativo.WS_EX_NOACTIVATE);
    }

    // Clic en otra ventana mientras se escribia: se devuelve el teclado y se cierra
    private void Ventana_Deactivated(object? sender, EventArgs e)
    {
        if (!_escribiendo) return;
        SoltarTeclado();
        if (EnPanel && !Zona.IsMouseOver && _modo != Modo.Bienvenida) Colapsar();
    }

    private void Ventana_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Renombrando un calendario: Enter guarda, Esc deshace (sin cerrar la isla)
        if (Keyboard.FocusedElement is TextBox { Tag: int indice } nombre && indice < _config.Calendarios.Count)
        {
            if (e.Key == Key.Escape) nombre.Text = _config.Calendarios[indice].Nombre;
            if (e.Key is Key.Enter or Key.Escape)
            {
                Keyboard.ClearFocus();   // LostKeyboardFocus guarda
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Escape)
        {
            if (_modo == Modo.Bienvenida) SoltarTeclado();   // deja de escribir, pero la bienvenida sigue
            else Colapsar();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && CampoCiudadBienvenida.IsKeyboardFocused)
        {
            AvanzarBienvenida(+1, desde: 40);   // guarda la ciudad
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && CampoCiudad.IsKeyboardFocused)
        {
            Keyboard.ClearFocus();   // confirma el campo (LostKeyboardFocus)
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && (CampoLinkCalendario.IsKeyboardFocused || CampoNombreCalendario.IsKeyboardFocused))
        {
            AgregarCalendario();
            e.Handled = true;
        }
    }

    private void CampoNotas_TextChanged(object sender, TextChangedEventArgs e)
    {
        PistaNotas.Visibility = CampoNotas.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _guardarNotas.Stop();   // se guarda 1 s despues de la ultima tecla
        _guardarNotas.Start();
    }

    // ---------- Estado de la PC ----------

    private async Task LeerPcAsync()
    {
        if (_leyendoPc) return;
        _leyendoPc = true;
        LecturaPc lectura;
        try { lectura = await Task.Run(_estadoPc.Leer); }
        finally { _leyendoPc = false; }
        if (_modo != Modo.Pc) return;

        // "—" mientras no hay dato real (primera lectura): mejor eso que un 0 % falso
        ValorCpu.Text = lectura.Cpu is { } cpu ? $"{cpu:0}%" : "—";
        Llenar(BarraCpu, lectura.Cpu ?? 0);
        ValorGpu.Text = lectura.Gpu is { } gpu ? $"{gpu:0}%" : "—";
        Llenar(BarraGpu, lectura.Gpu ?? 0);
        ValorRam.Text = $"{lectura.Ram:0}%";
        Llenar(BarraRam, lectura.Ram);
        DetalleRam.Text = $"{lectura.RamUsadaGb:0.0} / {lectura.RamTotalGb:0} GB";
        var t = lectura.Encendida;
        ValorEncendida.Text = t.TotalDays >= 1 ? $"{(int)t.TotalDays} d {t.Hours} h" : $"{t.Hours} h {t.Minutes} min";
    }

    private static void Llenar(Border barra, double porcentaje)
    {
        double ancho = ((FrameworkElement)barra.Parent).ActualWidth;
        // From explicito: sin el, la animacion parte del Width base, y si ese es NaN (auto) revienta
        barra.BeginAnimation(WidthProperty, new DoubleAnimation(barra.ActualWidth, ancho * Math.Clamp(porcentaje, 0, 100) / 100,
                                                                TimeSpan.FromMilliseconds(300)));
    }

    // ---------- Liberar espacio (temporales + papelera) ----------

    private bool _limpiando;
    private readonly DispatcherTimer _finLimpieza = new() { Interval = TimeSpan.FromSeconds(6) };

    private void AnalizarLimpieza_Click(object sender, RoutedEventArgs e) => _ = AnalizarLimpiezaAsync();

    /// <summary>Paso 1: solo mide (no borra nada) y pide confirmacion con el total.</summary>
    private async Task AnalizarLimpiezaAsync()
    {
        _finLimpieza.Stop();
        BotonAnalizarLimpieza.IsEnabled = false;
        EstadoLimpieza.Text = Idioma.T("Pc.Analizando");

        var analisis = await Task.Run(Limpieza.Analizar);
        BotonAnalizarLimpieza.IsEnabled = true;
        if (_modo != Modo.Pc) return;

        if (analisis.Total < 1024 * 1024)
        {
            MostrarFinLimpieza(Idioma.T("Pc.NadaQueLimpiar"));
            return;
        }

        EstadoLimpieza.Text = Idioma.T("Pc.Analisis", Limpieza.Formatear(analisis.BytesTemporales, Idioma.Cultura), Limpieza.Formatear(analisis.BytesPapelera, Idioma.Cultura));
        EstadoLimpieza.Foreground = Brushes.White;
        BotonConfirmarLimpieza.Content = Idioma.T("Pc.LiberarN", Limpieza.Formatear(analisis.Total, Idioma.Cultura));
        BotonAnalizarLimpieza.Visibility = Visibility.Collapsed;
        BotonCancelarLimpieza.Visibility = Visibility.Visible;
        BotonConfirmarLimpieza.Visibility = Visibility.Visible;
    }

    /// <summary>Paso 2 (irreversible): borra los temporales viejos y vacia la papelera.</summary>
    private async void ConfirmarLimpieza_Click(object sender, RoutedEventArgs e)
    {
        _limpiando = true;
        BotonCancelarLimpieza.Visibility = Visibility.Collapsed;
        BotonConfirmarLimpieza.Visibility = Visibility.Collapsed;
        EstadoLimpieza.Text = Idioma.T("Pc.Limpiando");
        try
        {
            var resultado = await Task.Run(Limpieza.Limpiar);
            string salteados = resultado.ArchivosSalteados > 0 ? Idioma.T("Pc.EnUso", resultado.ArchivosSalteados) : "";
            MostrarFinLimpieza(Idioma.T("Pc.Listo", Limpieza.Formatear(resultado.BytesLiberados, Idioma.Cultura), salteados));
        }
        finally
        {
            _limpiando = false;
        }
    }

    private void CancelarLimpieza_Click(object sender, RoutedEventArgs e) => ReiniciarLimpieza();

    private void MostrarFinLimpieza(string mensaje)
    {
        EstadoLimpieza.Text = mensaje;
        EstadoLimpieza.Foreground = Brushes.White;
        BotonAnalizarLimpieza.Visibility = Visibility.Collapsed;
        BotonCancelarLimpieza.Visibility = Visibility.Collapsed;
        BotonConfirmarLimpieza.Visibility = Visibility.Collapsed;
        _finLimpieza.Stop();
        _finLimpieza.Start();   // a los segundos vuelve al estado inicial
    }

    private void ReiniciarLimpieza()
    {
        _finLimpieza.Stop();
        EstadoLimpieza.Text = Idioma.T("Pc.TemporalesYPapelera");
        EstadoLimpieza.Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
        BotonAnalizarLimpieza.Visibility = Visibility.Visible;
        BotonAnalizarLimpieza.IsEnabled = true;
        BotonCancelarLimpieza.Visibility = Visibility.Collapsed;
        BotonConfirmarLimpieza.Visibility = Visibility.Collapsed;
    }

    // ---------- Clima ----------

    private void MostrarClima()
    {
        if (_clima?.Estado is not { } clima)
        {
            DatosClima.Visibility = Visibility.Collapsed;
            ProblemaClima.Visibility = Visibility.Visible;
            ProblemaClima.Text = _clima?.Problema ?? Idioma.T("Clima.Cargando");
            return;
        }

        DatosClima.Visibility = Visibility.Visible;
        ProblemaClima.Visibility = Visibility.Collapsed;
        var (texto, simbolo) = Clima.Describir(clima.Codigo, clima.EsDeDia);
        SimboloClima.Text = simbolo;
        SimboloClima.Foreground = clima.Codigo <= 1 && clima.EsDeDia
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x0A))
            : new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
        TemperaturaClima.Text = $"{clima.Temperatura:0}°";
        DescripcionClima.Text = texto;
        LugarClima.Text = clima.Lugar;
        MaxMinClima.Text = $"↑ {clima.Maxima:0}°   ↓ {clima.Minima:0}°";
        ExtraClima.Text = Idioma.T("Clima.Extra", clima.Sensacion.ToString("0"), clima.Humedad, clima.Viento.ToString("0"));
    }

    // ---------- Calendario ----------

    private DateTime _mesVisible = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateOnly? _diaElegido;   // null = a la derecha se muestran los proximos
    private const int MaxEventosVisibles = 4;
    private static readonly SolidColorBrush Tenue = new(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush Resalte = new(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));

    private void MostrarCalendario()
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var primero = _mesVisible;
        string formato = primero.Year == hoy.Year ? "MMMM" : "MMMM yyyy";
        MesCalendario.Text = Idioma.Cultura.TextInfo.ToTitleCase(primero.ToString(formato, Idioma.Cultura));
        // Solo se puede navegar dentro del rango cargado (2 meses atras, 6 adelante)
        MesAnterior.IsEnabled = primero > Modulos.Calendario.DesdeCargado;
        MesSiguiente.IsEnabled = primero.AddMonths(1) < Modulos.Calendario.HastaCargado;
        MesAnterior.Opacity = MesAnterior.IsEnabled ? 1 : 0.3;
        MesSiguiente.Opacity = MesSiguiente.IsEnabled ? 1 : 0.3;

        DiasCalendario.Children.Clear();
        InicialesCalendario.Children.Clear();
        foreach (var inicial in Idioma.T("Cal.Iniciales").Split(','))
            InicialesCalendario.Children.Add(new TextBlock
            {
                Text = inicial,
                Foreground = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Width = 22,
                Height = 12,   // fijo: AltoCalendario cuenta 12 + 3 de margen
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 3),
            });

        int vacios = ((int)primero.DayOfWeek + 6) % 7;   // la semana empieza el lunes
        for (int i = 0; i < vacios; i++) DiasCalendario.Children.Add(new Border());

        var colores = _calendario?.ColoresDelMes(primero.Year, primero.Month) ?? [];
        for (int dia = 1; dia <= DateTime.DaysInMonth(primero.Year, primero.Month); dia++)
        {
            var fecha = new DateOnly(primero.Year, primero.Month, dia);
            DiasCalendario.Children.Add(CeldaDia(fecha, fecha == hoy, fecha == _diaElegido, colores.GetValueOrDefault(fecha)));
        }

        MostrarEventos();

        // Los meses de 5 y 6 semanas miden distinto: la pildora se ajusta al navegar
        if (_modo == Modo.Calendario)
            Animar(Pildora, HeightProperty, AltoCalendario(), 260, new CubicEase { EasingMode = EasingMode.EaseOut });
    }

    private const double AltoFilaDia = 25;

    /// <summary>Alto del panel segun cuantas semanas ocupa el mes visible (cabecera + dias + margenes).</summary>
    private double AltoCalendario()
    {
        int vacios = ((int)_mesVisible.DayOfWeek + 6) % 7;
        int semanas = (int)Math.Ceiling((vacios + DateTime.DaysInMonth(_mesVisible.Year, _mesVisible.Month)) / 7.0);
        // margen sup + cabecera del mes (22 + 6) + iniciales (12 + 3) + semanas + margen inf (deja lugar a los puntitos de pagina)
        return 14 + 28 + 15 + semanas * AltoFilaDia + 20;
    }

    /// <summary>
    /// Numero del dia + un puntito por calendario con eventos (hasta 3).
    /// Hoy: circulo rojo (como los calendarios del celular). Elegido: fondo gris suave.
    /// </summary>
    private Border CeldaDia(DateOnly fecha, bool esHoy, bool elegido, List<Color>? colores)
    {
        var rojo = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
        var numero = new Border
        {
            Width = 19,
            Height = 19,
            CornerRadius = new CornerRadius(9.5),
            Background = esHoy ? rojo : elegido ? new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent,
            BorderBrush = esHoy && elegido ? Brushes.White : null,
            BorderThickness = new Thickness(esHoy && elegido ? 1.5 : 0),
            Child = new TextBlock
            {
                Text = fecha.Day.ToString(),
                FontSize = 10.5,
                FontWeight = esHoy || elegido ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = esHoy || elegido ? Brushes.White : new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var puntos = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Height = 4, Margin = new Thickness(0, 1, 0, 0) };
        foreach (var color in (colores ?? []).Take(3))
            puntos.Children.Add(new Ellipse { Width = 4, Height = 4, Margin = new Thickness(0.75, 0, 0.75, 0), Fill = new SolidColorBrush(color) });

        var pila = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        pila.Children.Add(numero);
        pila.Children.Add(puntos);

        var celda = new Border { Width = 22, Height = AltoFilaDia, CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Cursor = Cursors.Hand, Tag = fecha, Child = pila };
        celda.MouseEnter += (_, _) => celda.Background = Resalte;
        celda.MouseLeave += (_, _) => celda.Background = Brushes.Transparent;
        celda.MouseLeftButtonUp += Dia_Click;
        return celda;
    }

    /// <summary>Columna derecha: los proximos, o los eventos del dia elegido.</summary>
    private void MostrarEventos()
    {
        EventosCalendario.Children.Clear();
        VolverAProximos.Visibility = _diaElegido is null ? Visibility.Collapsed : Visibility.Visible;
        TituloEventos.Text = _diaElegido is { } elegido
            ? Capitalizar(elegido.ToString(Idioma.T("Fmt.FechaLarga"), Idioma.Cultura))
            : Idioma.T("Cal.Proximos");

        if (_calendario is null || (_calendario.Fuentes.Count == 0 && !Demo.Activo))   // en demo no hay links, hay eventos de ejemplo
        {
            EventosCalendario.Children.Add(new TextBlock { Text = Idioma.T("Cal.Vincula"), Foreground = Tenue, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            var boton = new Button { Content = Idioma.T("Cal.ComoVinculo"), Style = (Style)FindResource("BotonAccion"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
            boton.Click += Tutorial_Click;
            EventosCalendario.Children.Add(boton);
            return;
        }

        var eventos = _diaElegido is { } dia ? _calendario.DelDia(dia).ToList() : _calendario.Proximos.ToList();
        if (eventos.Count == 0)
        {
            string vacio = _calendario.ConError.Count == _calendario.Fuentes.Count ? Idioma.T("Cal.ErrorLeer")
                         : _diaElegido is null ? Idioma.T("Cal.NadaProximos")
                         : Idioma.T("Cal.SinEventosDia");
            EventosCalendario.Children.Add(new TextBlock { Text = vacio, Foreground = Tenue, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            return;
        }

        foreach (var evento in eventos.Take(MaxEventosVisibles))
            EventosCalendario.Children.Add(FilaEvento(evento, conDia: _diaElegido is null));
        if (eventos.Count > MaxEventosVisibles)
            EventosCalendario.Children.Add(new TextBlock { Text = Idioma.T("Cal.Mas", eventos.Count - MaxEventosVisibles), Foreground = Tenue, FontSize = 10.5, Margin = new Thickness(0, 1, 0, 0) });
    }

    /// <summary>
    /// Un evento: barrita del color de su calendario + titulo (lo importante, arriba) + cuando (tenue, abajo).
    /// Si viene de Google u Outlook, al hacer clic abre ese dia en su web.
    /// </summary>
    private Border FilaEvento(Evento evento, bool conDia)
    {
        string hora = evento.TodoElDia ? Idioma.T("Cal.TodoElDia") : evento.Inicio.ToString("HH:mm");

        var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textos.Children.Add(new TextBlock { Text = evento.Titulo, Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        textos.Children.Add(new TextBlock { Text = conDia ? CuandoEs(evento) : hora, Foreground = Tenue, FontSize = 10.5, Margin = new Thickness(0, 1, 0, 0) });

        var contenido = new Grid();
        contenido.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        contenido.ColumnDefinitions.Add(new ColumnDefinition());
        contenido.Children.Add(new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(evento.Color), Margin = new Thickness(0, 2, 9, 2) });
        Grid.SetColumn(textos, 1);
        contenido.Children.Add(textos);

        var fila = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(-6, 0, 0, 2), Background = Brushes.Transparent, Child = contenido };
        if (evento.Servicio == Servicio.Otro) return fila;

        fila.Cursor = Cursors.Hand;
        fila.ToolTip = Idioma.T(evento.Servicio == Servicio.Google ? "Cal.AbrirGoogle" : "Cal.AbrirOutlook");
        fila.MouseEnter += (_, _) => fila.Background = Resalte;
        fila.MouseLeave += (_, _) => fila.Background = Brushes.Transparent;
        fila.MouseLeftButtonUp += (_, _) => AbrirDiaEnWeb(evento);
        return fila;
    }

    private static void AbrirDiaEnWeb(Evento evento)
    {
        var f = evento.Inicio;
        string url = evento.Servicio == Servicio.Google
            ? $"https://calendar.google.com/calendar/r/day/{f.Year}/{f.Month}/{f.Day}"
            : $"https://outlook.live.com/calendar/0/view/day/{f.Year}/{f.Month}/{f.Day}";
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Escribir($"calendario: no se pudo abrir el navegador: {ex.Message}"); }
    }

    // Clic en un dia: muestra sus eventos; otro clic en el mismo vuelve a los proximos
    private void Dia_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: DateOnly fecha }) return;
        _diaElegido = _diaElegido == fecha ? null : fecha;
        MostrarCalendario();
    }

    private void MesAnterior_Click(object sender, RoutedEventArgs e)
    {
        _mesVisible = _mesVisible.AddMonths(-1);
        MostrarCalendario();
    }

    private void MesSiguiente_Click(object sender, RoutedEventArgs e)
    {
        _mesVisible = _mesVisible.AddMonths(1);
        MostrarCalendario();
    }

    // Clic en el nombre del mes: volver a hoy
    private void MesCalendario_Click(object sender, RoutedEventArgs e) => VolverAHoy();

    private void VolverAProximos_Click(object sender, RoutedEventArgs e)
    {
        _diaElegido = null;
        MostrarCalendario();
    }

    private void VolverAHoy()
    {
        _mesVisible = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _diaElegido = null;
        MostrarCalendario();
    }

    // ---------- Tutorial para vincular el calendario ----------

    private static readonly Dictionary<string, (string Url, string[] Pasos)> Tutoriales = new()
    {
        ["google"] = ("https://calendar.google.com/calendar/r/settings",
        [
            "Tut.Google1",
            "Tut.Google2",
            "Tut.Google3",
            "Tut.Google4",
        ]),
        ["outlook"] = ("https://outlook.live.com/calendar/0/options/calendar/SharedCalendars",
        [
            "Tut.Outlook1",
            "Tut.Outlook2",
            "Tut.Outlook3",
            "Tut.Outlook4",
        ]),
    };

    private string _tutorialProveedor = "google";

    private void Tutorial_Click(object sender, RoutedEventArgs e)
    {
        if (_escribiendo) SoltarTeclado();
        TutorialGoogle.IsChecked = true;
        _tutorialProveedor = "google";
        MostrarPasos();
        Expandir(Modo.Tutorial, deslizarDesde: 40);
    }

    private void TutorialProveedor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string proveedor }) return;
        _tutorialProveedor = proveedor;
        MostrarPasos();
    }

    private void MostrarPasos()
    {
        PasosTutorial.Children.Clear();
        int numero = 1;
        foreach (var paso in Tutoriales[_tutorialProveedor].Pasos)
        {
            var fila = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fila.ColumnDefinitions.Add(new ColumnDefinition());
            fila.Children.Add(new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromArgb(0x40, 0x0A, 0x84, 0xFF)),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = (numero++).ToString(),
                    Foreground = Brushes.White,
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
            var texto = new TextBlock { Text = Idioma.T(paso), Foreground = Brushes.White, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 1, 0, 0) };
            Grid.SetColumn(texto, 1);
            fila.Children.Add(texto);
            PasosTutorial.Children.Add(fila);
        }
    }

    private void AbrirConfigCalendario_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Tutoriales[_tutorialProveedor].Url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Escribir($"tutorial: no se pudo abrir el navegador: {ex.Message}");
        }
    }

    // Lleva al panel de calendarios con el cursor en el campo del link, listo para pegar
    private void YaTengoElLink_Click(object sender, RoutedEventArgs e)
    {
        Expandir(Modo.Calendarios, deslizarDesde: 40);
        TomarTeclado();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => CampoLinkCalendario.Focus());
    }

    /// <summary>Solo la primera letra en mayuscula ("jueves 30 de octubre" → "Jueves 30 de octubre").</summary>
    private static string Capitalizar(string texto) =>
        texto.Length == 0 ? texto : char.ToUpper(texto[0], Idioma.Cultura) + texto[1..];

    private static string CuandoEs(Evento evento)
    {
        int dias = (evento.Inicio.Date - DateTime.Today).Days;
        string dia = dias switch
        {
            0 => Idioma.T("Cal.Hoy"),
            1 => Idioma.T("Cal.Manana"),
            _ => Idioma.Cultura.TextInfo.ToTitleCase(evento.Inicio.ToString("ddd d", Idioma.Cultura)),
        };
        return evento.TodoElDia ? dia : $"{dia} · {evento.Inicio:HH:mm}";
    }
}
