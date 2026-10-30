using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskIsleBar.Modulos;

/// <summary>
/// Clic en una notificacion de la isla: hace lo mismo que Windows al tocar la suya.
/// 1) Notificacion de protocolo ("activationType=protocol"): abre ese link.
/// 2) La app registro un activador (ToastActivatorCLSID en su acceso directo, ej. Chrome y sus apps como WhatsApp
///    Web): se le pasan los argumentos de la notificacion, asi abre el chat o la pagina justa.
/// 3) Si no (ej. Discord, que recibe el clic dentro de su propio proceso): se trae la app al frente.
/// Los argumentos solo van a la app que mando la notificacion y nunca al log.
/// </summary>
public static class AbrirNotificacion
{
    // Esquemas que no se abren aunque una notificacion los pida: ejecutan o buscan archivos locales
    private static readonly string[] EsquemasBloqueados =
        ["file", "shell", "search-ms", "search", "ms-msdt", "ms-officecmd", "ms-appinstaller", "javascript", "vbscript"];

    private static readonly Dictionary<string, Guid?> Activadores = new(StringComparer.OrdinalIgnoreCase);
    private static bool _activadoresLeidos;

    public static void Abrir(string aumid, string? activacion, string? argumentos)
    {
        AllowSetForegroundWindow(ASFW_ANY);   // la isla recibio el clic: le cede el frente a la app que se abre
        Task.Run(() =>
        {
            try
            {
                if (activacion == "protocol" && AbrirProtocolo(argumentos)) return;
                if (activacion is null or "foreground" && !string.IsNullOrEmpty(argumentos) &&
                    ActivadorDe(aumid) is { } clsid && Activar(clsid, aumid, argumentos)) return;
                Application.Current?.Dispatcher.InvokeAsync(() => TraerApp(aumid));
            }
            catch (Exception ex)
            {
                Log.Escribir($"notificaciones: no se pudo abrir: {ex.GetType().Name}");
            }
        });
    }

    private static bool AbrirProtocolo(string? link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) ||
            EsquemasBloqueados.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)) return false;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        Log.Escribir($"notificaciones: abierta por protocolo ({uri.Scheme})");
        return true;
    }

    private static bool Activar(Guid clsid, string aumid, string argumentos)
    {
        object? objeto = null;
        try
        {
            objeto = Activator.CreateInstance(Type.GetTypeFromCLSID(clsid, throwOnError: true)!);
            ((INotificationActivationCallback)objeto!).Activate(aumid, argumentos, IntPtr.Zero, 0);
            Log.Escribir("notificaciones: abierta por el activador de la app");
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or TypeLoadException)
        {
            Log.Escribir($"notificaciones: el activador de la app fallo ({ex.GetType().Name}), se trae la app");
            return false;
        }
        finally
        {
            if (objeto is not null && Marshal.IsComObject(objeto)) Marshal.ReleaseComObject(objeto);
        }
    }

    private static void TraerApp(string aumid)
    {
        // Por el id de la ventana (apps de Chrome) o por el nombre del proceso: Discord y otras apps de Electron
        // no le ponen el id a su ventana ("com.squirrel.Discord.Discord" → proceso "Discord")
        if (Nativo.TraerAlFrentePorAppId(aumid) || Nativo.TraerAlFrente(Musica.NombreDeApp(aumid)))
        {
            Log.Escribir("notificaciones: app traida al frente");
            return;
        }
        // Cerrada o escondida en la bandeja: se la abre (si ya corre, la nueva instancia le pide que se muestre)
        Log.Escribir("notificaciones: app abierta por su acceso directo");
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"shell:AppsFolder\\{aumid}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Escribir($"notificaciones: no se pudo abrir la app: {ex.GetType().Name}");
        }
    }

    // ---------- Activador registrado (acceso directo del menu Inicio) ----------

    /// <summary>
    /// Windows encuentra el activador de una app de escritorio en su acceso directo del menu Inicio
    /// (propiedades AppUserModel.ID y AppUserModel.ToastActivatorCLSID). Se leen una vez y quedan en memoria.
    /// </summary>
    private static Guid? ActivadorDe(string aumid)
    {
        lock (Activadores)
        {
            if (!_activadoresLeidos)
            {
                _activadoresLeidos = true;
                foreach (var carpeta in new[] { Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.Programs })
                {
                    string raiz = Environment.GetFolderPath(carpeta);
                    if (raiz.Length == 0 || !Directory.Exists(raiz)) continue;
                    foreach (var acceso in Directory.EnumerateFiles(raiz, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                        if (LeerAcceso(acceso) is ({ } id, { } clsid)) Activadores.TryAdd(id, clsid);
                }
            }
            return Activadores.GetValueOrDefault(aumid);
        }
    }

    private static (string? Id, Guid? Activador) LeerAcceso(string ruta)
    {
        var iid = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreFromParsingName(ruta, IntPtr.Zero, 0, ref iid, out var propiedades) != 0) return (null, null);
        try
        {
            string? id = null;
            Guid? clsid = null;
            var clave = new PROPERTYKEY { fmtid = AppUserModel, pid = 5 };   // System.AppUserModel.ID
            var valor = new PROPVARIANT();
            if (propiedades.GetValue(ref clave, valor) == 0 && valor.vt == VT_LPWSTR) id = Marshal.PtrToStringUni(valor.p);
            PropVariantClear(valor);

            clave = new PROPERTYKEY { fmtid = AppUserModel, pid = 26 };      // System.AppUserModel.ToastActivatorCLSID
            valor = new PROPVARIANT();
            if (propiedades.GetValue(ref clave, valor) == 0 && valor.vt == VT_CLSID) clsid = Marshal.PtrToStructure<Guid>(valor.p);
            PropVariantClear(valor);
            return (id, clsid);
        }
        finally
        {
            Marshal.ReleaseComObject(propiedades);
        }
    }

    // ---------- Icono de la app ----------

    private static readonly Dictionary<string, ImageSource?> Iconos = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Icono de la app por su AppUserModelId ("shell:AppsFolder"), o null. Desde el hilo de la ventana (STA).</summary>
    public static ImageSource? IconoDe(string aumid)
    {
        if (Iconos.TryGetValue(aumid, out var icono)) return icono;
        icono = LeerIcono(aumid);
        Iconos[aumid] = icono;
        return icono;
    }

    private static ImageSource? LeerIcono(string aumid)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName($@"shell:AppsFolder\{aumid}", IntPtr.Zero, ref iid, out var fabrica) != 0) return null;
        IntPtr hbitmap = IntPtr.Zero;
        try
        {
            if (fabrica.GetImage(new SIZE { cx = 64, cy = 64 }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out hbitmap) != 0) return null;
            return DesdeHBitmap(hbitmap);
        }
        catch (COMException) { return null; }
        finally
        {
            if (hbitmap != IntPtr.Zero) DeleteObject(hbitmap);
            Marshal.ReleaseComObject(fabrica);
        }
    }

    /// <summary>El shell devuelve un DIB de 32 bits con alfa premultiplicado (de abajo hacia arriba).</summary>
    private static BitmapSource? DesdeHBitmap(IntPtr hbitmap)
    {
        if (GetObject(hbitmap, Marshal.SizeOf<BITMAP>(), out var info) == 0 || info.bmBits == IntPtr.Zero || info.bmBitsPixel != 32) return null;
        int alto = Math.Abs(info.bmHeight), stride = info.bmWidthBytes;
        var pixeles = new byte[stride * alto];
        Marshal.Copy(info.bmBits, pixeles, 0, pixeles.Length);
        if (info.bmHeight > 0)
        {
            // De abajo hacia arriba: se dan vuelta las filas
            var fila = new byte[stride];
            for (int i = 0; i < alto / 2; i++)
            {
                Buffer.BlockCopy(pixeles, i * stride, fila, 0, stride);
                Buffer.BlockCopy(pixeles, (alto - 1 - i) * stride, pixeles, i * stride, stride);
                Buffer.BlockCopy(fila, 0, pixeles, (alto - 1 - i) * stride, stride);
            }
        }
        var imagen = BitmapSource.Create(info.bmWidth, alto, 96, 96, PixelFormats.Pbgra32, null, pixeles, stride);
        imagen.Freeze();
        return imagen;
    }

    // ---------- Interop ----------

    private const int ASFW_ANY = -1;
    private const ushort VT_LPWSTR = 31, VT_CLSID = 72;
    private const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_ICONONLY = 0x4;
    private static readonly Guid AppUserModel = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int proceso);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr objeto);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr objeto, int tamaño, out BITMAP bitmap);
    [DllImport("ole32.dll")] private static extern int PropVariantClear([In, Out] PROPVARIANT valor);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetPropertyStoreFromParsingName(string ruta, IntPtr contexto, int opciones, ref Guid iid, out IPropertyStore propiedades);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string ruta, IntPtr contexto, ref Guid iid, out IShellItemImageFactory fabrica);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class PROPVARIANT { public ushort vt; public ushort r1, r2, r3; public IntPtr p; public IntPtr p2; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint cantidad);
        [PreserveSig] int GetAt(uint indice, out PROPERTYKEY clave);
        [PreserveSig] int GetValue(ref PROPERTYKEY clave, [Out] PROPVARIANT valor);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE tamaño, int opciones, out IntPtr hbitmap);
    }

    [ComImport, Guid("53E31837-6600-4A81-9395-75CFFE746F94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface INotificationActivationCallback
    {
        void Activate([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, [MarshalAs(UnmanagedType.LPWStr)] string invokedArgs,
                      IntPtr datos, uint cantidad);
    }
}
