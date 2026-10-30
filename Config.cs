using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeskIsleBar;

/// <summary>Un calendario vinculado. El link va cifrado (DPAPI): funciona como contraseña de solo lectura.</summary>
public class CalendarioVinculado
{
    public string Nombre { get; set; } = "";
    public string LinkCifrado { get; set; } = "";
    public string Color { get; set; } = "#FF9F0A";
}

/// <summary>Preferencias del usuario, en %LOCALAPPDATA%\DeskIsleBar\config.json.</summary>
public class Config
{
    private static readonly string Archivo = Rutas.Archivo("config.json");

    public const int MaxCalendarios = 6;
    /// <summary>Colores que se van asignando a los calendarios (naranja, azul, verde, rosa, violeta, amarillo).</summary>
    public static readonly string[] PaletaCalendarios = ["#FF9F0A", "#0A84FF", "#30D158", "#FF375F", "#BF5AF2", "#FFD60A"];

    /// <summary>"auto" = el idioma de Windows (si esta disponible); o un codigo de <see cref="DeskIsleBar.Idioma.Disponibles"/>.</summary>
    public string Idioma { get; set; } = "auto";

    /// <summary>Clave de <see cref="Modulos.Musica.Apps"/>: "auto" = la que este sonando.</summary>
    public string AppMusica { get; set; } = "auto";

    // Paneles de la ronda que se pueden mostrar u ocultar (Musica y Accesos estan siempre)
    public bool PanelCalendario { get; set; } = true;
    public bool PanelClima { get; set; } = true;
    public bool PanelPc { get; set; } = true;
    public bool PanelNotas { get; set; } = true;

    /// <summary>Mostrar en la isla las notificaciones de las apps (Chrome, Telegram...).</summary>
    public bool NotificacionesWindows { get; set; } = true;

    /// <summary>Con "No molestar" de Windows prendido no se muestran. Apagado: la isla reemplaza los carteles de Windows.</summary>
    public bool RespetarNoMolestar { get; set; }

    /// <summary>
    /// "auto" = encima, salvo juegos a pantalla completa (sobre videos solo avisos); "siempre" = encima de todo;
    /// "detras" = debajo de las apps, al frente solo mientras muestra algo.
    /// </summary>
    public string PrimerPlano { get; set; } = "auto";

    /// <summary>Tipos de aviso apagados en Opciones → Avisos (ver <see cref="TiposAviso"/>). Vacio = todos prendidos.</summary>
    public List<string> AvisosApagados { get; set; } = [];

    /// <summary>Minutos del temporizador personalizado (el ultimo elegido).</summary>
    public int TemporizadorPersonalizado { get; set; } = 25;

    /// <summary>Durante el bloque de estudio del Pomodoro los avisos comunes esperan al descanso.</summary>
    public bool FocoPomodoro { get; set; } = true;

    public string Ciudad { get; set; } = "";

    /// <summary>Ya vio (o salteo) la bienvenida: no se vuelve a mostrar sola, tampoco al actualizar.</summary>
    public bool BienvenidaVista { get; set; }
    public List<CalendarioVinculado> Calendarios { get; set; } = [];

    /// <summary>Formato viejo (un solo link, sin cifrar): se migra a <see cref="Calendarios"/> al cargar.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CalendarioIcs { get; set; }

    public static Config Cargar()
    {
        Config config;
        try { config = JsonSerializer.Deserialize<Config>(File.ReadAllText(Archivo)) ?? new Config(); }
        catch (Exception) { return new Config(); }   // no existe o esta roto: valores por defecto

        if (!string.IsNullOrWhiteSpace(config.CalendarioIcs))
        {
            config.Calendarios.Add(new CalendarioVinculado
            {
                Nombre = "Mi calendario",
                LinkCifrado = Secreto.Cifrar(config.CalendarioIcs.Trim()),
                Color = PaletaCalendarios[0],
            });
            config.CalendarioIcs = null;
            config.Guardar();   // el link en texto plano desaparece del disco
            Log.Escribir("config: link de calendario migrado a la lista (cifrado)");
        }
        return config;
    }

    public void Guardar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Archivo)!);
            // Escritura atomica: si se corta a la mitad, el config anterior queda intacto
            string temporal = Archivo + ".tmp";
            File.WriteAllText(temporal, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporal, Archivo, overwrite: true);
        }
        catch (IOException ex)
        {
            Log.Escribir($"config: no se pudo guardar: {ex.Message}");
        }
    }
}
