using System.Windows.Media;
using DeskIsleBar.Modulos;

namespace DeskIsleBar;

/// <summary>
/// Modo demo (--demo), para capturas y videos: calendario, nota y ciudad de ejemplo en lugar de los del usuario.
/// No se guarda nada: el config, las notas y los calendarios reales quedan como estaban.
/// </summary>
static class Demo
{
    public static bool Activo { get; set; }

    public const string Ciudad = "Barcelona";

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
