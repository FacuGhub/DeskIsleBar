using System.Windows.Media;

namespace DeskIsleBar;

/// <summary>Lo que la isla muestra al expandirse. Cualquier modulo puede generar uno.</summary>
/// <param name="Icono">Glifo de Segoe Fluent Icons.</param>
/// <param name="Carga">0..1 para dibujar la bateria; null la oculta.</param>
/// <param name="Persistente">true = queda expandida hasta que se la toque (y suena).</param>
/// <param name="Segundos">Cuanto queda expandida si no es persistente.</param>
/// <param name="Tipo">Clase de aviso que se puede apagar en Opciones → Avisos (ver <see cref="TiposAviso"/>); null = siempre se muestra.</param>
/// <param name="Imagen">Icono de la app (notificaciones): reemplaza al glifo.</param>
/// <param name="AlTocar">Que hacer al hacerle clic (ej. abrir la app o el chat de la notificacion); null = solo se cierra.</param>
public record Aviso(string Icono, string Titulo, string Detalle, Color Acento, double? Carga, bool Persistente, double Segundos = 8,
                    string? Tipo = null, ImageSource? Imagen = null, Action? AlTocar = null);

/// <summary>Los avisos que se pueden prender o apagar, en el orden de la pantalla Avisos, con su clave de texto.</summary>
public static class TiposAviso
{
    public const string Baterias = "baterias", Dispositivos = "dispositivos", Recordatorios = "recordatorios", Captura = "captura";

    /// <summary>No es un aviso: los puntitos de camara/microfono/Bloq Mayus en la isla. Se apaga en la misma pantalla.</summary>
    public const string Indicadores = "indicadores";

    public static readonly (string Tipo, string Clave)[] Todos =
    [
        (Baterias, "Av.Op.Baterias"),
        (Dispositivos, "Av.Op.Dispositivos"),
        (Recordatorios, "Av.Op.Recordatorios"),
        (Modulos.AvisosSistema.TipoCamara, "Av.Op.Camara"),
        (Modulos.AvisosSistema.TipoBateriaPc, "Av.Op.BateriaPc"),
        (Modulos.AvisosSistema.TipoBloqMayus, "Av.Op.BloqMayus"),
        (Modulos.AvisosSistema.TipoInternet, "Av.Op.Internet"),
        (Captura, "Av.Op.Captura"),
        (Indicadores, "Av.Op.Indicadores"),
    ];
}
