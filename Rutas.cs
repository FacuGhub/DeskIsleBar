using System.IO;

namespace DeskIsleBar;

/// <summary>
/// Carpeta de datos del usuario (config, notas, posicion, registro). Instalada con el instalador:
/// %LOCALAPPDATA%\DeskIsleBar. Empaquetada (Store): la carpeta local del paquete (Packages\...\LocalState), que
/// Windows borra al desinstalar; asi el Explorador y el Bloc de notas la ven igual que la app (las escrituras en
/// %LOCALAPPDATA% de un paquete van a otra carpeta virtual y "abrir la carpeta" no encontraria los archivos).
/// </summary>
static class Rutas
{
    public static readonly string Datos = Empaquetado.Activo
        ? Windows.Storage.ApplicationData.Current.LocalFolder.Path
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskIsleBar");

    public static string Archivo(string nombre) => Path.Combine(Datos, nombre);
}
