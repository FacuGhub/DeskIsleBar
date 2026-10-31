using System.Windows.Media;
using DeskIsleBar.Modulos;

namespace DeskIsleBar;

/// <summary>
/// Modo demo (--demo), para capturas y videos: musica, calendario, nota y ciudad de ejemplo en lugar de los del usuario.
/// No se guarda nada: el config, las notas y los calendarios reales quedan como estaban.
/// </summary>
static class Demo
{
    public static bool Activo { get; set; }

    public const string Ciudad = "Barcelona";

    /// <summary>Tema inventado (titulo, artista y portada), para no mostrar musica ni portadas con derechos.</summary>
    public static EstadoMusica Musica() =>
        new("Midnight Drive", "Neon Coast", Portada(), Reproduciendo: true, PuedeAnterior: true, PuedeSiguiente: true, PuedeSaltar: true);

    /// <summary>Portada estilo synthwave: cielo en degrade, sol con franjas y el horizonte.</summary>
    private static System.Windows.Media.Imaging.BitmapImage Portada()
    {
        const int lado = 300;
        var dibujo = new DrawingVisual();
        using (var g = dibujo.RenderOpen())
        {
            var cielo = new LinearGradientBrush(Color.FromRgb(0x2B, 0x10, 0x55), Color.FromRgb(0xF2, 0x5C, 0x7A), 90);
            g.DrawRectangle(cielo, null, new System.Windows.Rect(0, 0, lado, lado));
            var sol = new LinearGradientBrush(Color.FromRgb(0xFF, 0xD3, 0x6E), Color.FromRgb(0xFF, 0x6B, 0x5A), 90);
            g.PushClip(new RectangleGeometry(new System.Windows.Rect(0, 0, lado, 200)));
            g.DrawEllipse(sol, null, new System.Windows.Point(150, 175), 92, 92);
            g.Pop();
            // Franjas del sol (cortes del color del cielo)
            for (int i = 0; i < 4; i++)
                g.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xC9, 0x4B, 0x84)), null, new System.Windows.Rect(40, 140 + i * 14, 220, 4 + i));
            // Suelo con lineas de perspectiva
            g.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1A, 0x0B, 0x33)), null, new System.Windows.Rect(0, 200, lado, 100));
            var linea = new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0x5C, 0xC8)), 1.5);
            for (int i = 0; i < 6; i++) g.DrawLine(linea, new System.Windows.Point(0, 204 + i * i * 3.2), new System.Windows.Point(lado, 204 + i * i * 3.2));
            for (int i = -6; i <= 6; i++) g.DrawLine(linea, new System.Windows.Point(150 + i * 12, 200), new System.Windows.Point(150 + i * 70, 300));
        }
        var mapa = new System.Windows.Media.Imaging.RenderTargetBitmap(lado, lado, 96, 96, PixelFormats.Pbgra32);
        mapa.Render(dibujo);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(mapa));
        var memoria = new System.IO.MemoryStream();
        png.Save(memoria);
        memoria.Position = 0;
        var imagen = new System.Windows.Media.Imaging.BitmapImage();
        imagen.BeginInit();
        imagen.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        imagen.StreamSource = memoria;
        imagen.EndInit();
        imagen.Freeze();
        return imagen;
    }

    public const string Nota = "TODO:\n☕ coffee  ·  💧 drink water  ·  🌱 touch grass\nStop opening the island just to look at it";

    /// <summary>Eventos relativos a hoy, para que siempre haya proximos y puntitos en el mes.</summary>
    public static IEnumerable<Evento> Eventos()
    {
        var hoy = DateTime.Today;
        Color Paleta(int i) => (Color)ColorConverter.ConvertFromString(Config.PaletaCalendarios[i]);
        Evento E(int dias, int hora, int minuto, string titulo, int color, Servicio servicio = Servicio.Google) =>
            new(hoy.AddDays(dias).AddHours(hora).AddMinutes(minuto), TodoElDia: false, titulo, Paleta(color), servicio);

        // Uno que empieza en unos minutos: siempre hay un proximo hoy y dispara el recordatorio (sirve para probarlo)
        var pronto = DateTime.Now.AddMinutes(8);
        yield return new Evento(new DateTime(pronto.Year, pronto.Month, pronto.Day, pronto.Hour, pronto.Minute, 0), TodoElDia: false,
                                "Call with Ana", Paleta(1), Servicio.Google);
        yield return E(0, 18, 30, "Gym", 2);
        yield return E(1, 10, 0, "Team meeting", 1, Servicio.Outlook);
        // Sin emojis: WPF los dibuja en blanco y negro y en la lista de eventos quedan raros
        yield return E(2, 21, 0, "Pizza night", 0);
        yield return new Evento(hoy.AddDays(4), TodoElDia: true, "Mom's birthday", Paleta(3), Servicio.Google);
        yield return E(6, 9, 15, "Dentist", 4);
        yield return E(9, 20, 0, "Football with friends", 2);
        yield return E(13, 11, 0, "Project demo", 1, Servicio.Outlook);
        yield return E(17, 19, 30, "Concert", 5);
        yield return E(24, 8, 0, "Flight to Lisbon", 0);
    }
}
