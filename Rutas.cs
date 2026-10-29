using System.IO;

namespace DeskIsleBar;

/// <summary>Carpeta de datos del usuario (config, notas, posicion): %LOCALAPPDATA%\DeskIsleBar.</summary>
static class Rutas
{
    private static readonly string Base = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static readonly string Datos = Path.Combine(Base, "DeskIsleBar");

    public static string Archivo(string nombre) => Path.Combine(Datos, nombre);
}
