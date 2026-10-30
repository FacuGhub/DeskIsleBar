using System.IO;

namespace DeskIsleBar;

/// <summary>
/// Registro tecnico en %LOCALAPPDATA%\DeskIsleBar\deskislebar.log (con los datos del usuario, no al lado del .exe:
/// instalada en Archivos de programa o como MSIX, esa carpeta es de solo lectura). Nunca corta la app.
/// </summary>
static class Log
{
    private static readonly string Archivo = Rutas.Archivo("deskislebar.log");
    private const long TamañoMaximo = 2 * 1024 * 1024;   // al pasar los 2 MB se guarda como .old y se empieza de nuevo

    // El log se comparte para pedir ayuda: los mensajes de error de Windows pueden traer rutas con el usuario
    private static readonly string CarpetaUsuario = $@"\Users\{Environment.UserName}";

    public static void Escribir(string texto)
    {
        if (Environment.UserName.Length >= 2) texto = texto.Replace(CarpetaUsuario, @"\Users\<usuario>", StringComparison.OrdinalIgnoreCase);
        try
        {
            Directory.CreateDirectory(Rutas.Datos);
            var info = new FileInfo(Archivo);
            if (info.Exists && info.Length > TamañoMaximo) File.Move(Archivo, Archivo + ".old", overwrite: true);
            File.AppendAllText(Archivo, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {texto}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
