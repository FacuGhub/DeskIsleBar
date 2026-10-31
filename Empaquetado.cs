using System.Runtime.InteropServices;

namespace DeskIsleBar;

/// <summary>
/// Si la isla corre como paquete MSIX (instalada desde la Microsoft Store). Una sola app que se adapta: empaquetada,
/// las actualizaciones las maneja la Store y "Arrancar con Windows" usa la tarea de inicio del paquete; instalada
/// con el instalador de GitHub, todo sigue como siempre.
/// </summary>
static class Empaquetado
{
    /// <summary>Id de DeskIsleBar en la Microsoft Store (Partner Center → Identidad del producto).</summary>
    public const string IdStore = "9P54HP5M8FZ7";

    /// <summary>Ficha de la app en la Store (la abre la app de la Store, con su boton de actualizar).</summary>
    public const string FichaStore = "ms-windows-store://pdp/?productid=" + IdStore;

    public static readonly bool Activo = TienePaquete();

    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int largo, char[]? nombre);

    private static bool TienePaquete()
    {
        int largo = 0;
        return GetCurrentPackageFullName(ref largo, null) != APPMODEL_ERROR_NO_PACKAGE;
    }
}
