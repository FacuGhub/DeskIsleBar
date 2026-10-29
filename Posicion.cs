using System.Globalization;
using System.IO;
using System.Windows;

namespace DeskIsleBar;

/// <summary>
/// Donde dejo el usuario la isla y hacia donde crece desde ahi (anclaje). Sin archivo = centrada arriba
/// en el monitor principal. Formato: "left;top;horizontal;vertical" (los archivos viejos solo tienen left;top).
/// </summary>
static class Posicion
{
    private static readonly string Archivo = Rutas.Archivo("posicion.txt");

    public record Lugar(double Left, double Top, HorizontalAlignment Horizontal, VerticalAlignment Vertical);

    public static Lugar? Leer()
    {
        try
        {
            var partes = File.ReadAllText(Archivo).Trim().Split(';');
            var cultura = CultureInfo.InvariantCulture;
            return new Lugar(
                double.Parse(partes[0], cultura),
                double.Parse(partes[1], cultura),
                partes.Length > 2 && Enum.TryParse(partes[2], out HorizontalAlignment h) ? h : HorizontalAlignment.Center,
                partes.Length > 3 && Enum.TryParse(partes[3], out VerticalAlignment v) ? v : VerticalAlignment.Top);
        }
        catch (Exception) { return null; }   // no existe o esta roto: se usa el centro
    }

    public static void Guardar(Lugar lugar)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Archivo)!);
        File.WriteAllText(Archivo, string.Create(CultureInfo.InvariantCulture,
            $"{lugar.Left};{lugar.Top};{lugar.Horizontal};{lugar.Vertical}"));
    }

    public static void Borrar()
    {
        try { File.Delete(Archivo); } catch (IOException) { }
    }
}
