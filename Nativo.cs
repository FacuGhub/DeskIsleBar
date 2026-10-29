using System.Runtime.InteropServices;
using System.Text;

namespace DeskIsleBar;

static class Nativo
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_APPWINDOW = 0x00040000;

    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;

    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int estado);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder nombre, int max);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int indice);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hwnd, int indice, int valor);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr despuesDe, int x, int y, int cx, int cy, uint flags);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    private const uint GW_OWNER = 4;
    private const int SW_RESTORE = 9;

    /// <summary>
    /// Trae al frente la ventana principal de un proceso si esta visible (abierta o minimizada).
    /// false si no hay ninguna visible (ej. la app esta escondida en la bandeja) o Windows no lo permitio.
    /// </summary>
    public static bool TraerAlFrente(string nombreProceso)
    {
        var pids = System.Diagnostics.Process.GetProcessesByName(nombreProceso).Select(p => (uint)p.Id).ToHashSet();
        if (pids.Count == 0) return false;

        IntPtr encontrada = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            // Ventana principal: del proceso, visible, sin dueño y con titulo
            if (pids.Contains(pid) && IsWindowVisible(hwnd) && GetWindow(hwnd, GW_OWNER) == IntPtr.Zero && GetWindowTextLength(hwnd) > 0)
            {
                encontrada = hwnd;
                return false;   // cortar la enumeracion
            }
            return true;
        }, IntPtr.Zero);

        return Activar(encontrada);
    }

    /// <summary>
    /// Trae al frente la ventana que tiene ese AppUserModelId propio. Lo usan las apps instaladas desde
    /// Chrome/Edge (YouTube Music es "Chrome._crx_..."): corren dentro de chrome.exe, asi que por nombre de
    /// proceso no se encuentran y se terminaba abriendo otra ventana.
    /// </summary>
    public static bool TraerAlFrentePorAppId(string appUserModelId)
    {
        IntPtr encontrada = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && GetWindow(hwnd, GW_OWNER) == IntPtr.Zero &&
                string.Equals(AppIdDeVentana(hwnd), appUserModelId, StringComparison.OrdinalIgnoreCase))
            {
                encontrada = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return Activar(encontrada);
    }

    private static bool Activar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        return SetForegroundWindow(hwnd);
    }

    // ---------- AppUserModelId de una ventana (propiedad System.AppUserModel.ID) ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT { public ushort vt; public ushort r1, r2, r3; public IntPtr valor; public IntPtr extra; }

    private const ushort VT_LPWSTR = 31;

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint cantidad);
        [PreserveSig] int GetAt(uint indice, out PROPERTYKEY clave);
        [PreserveSig] int GetValue(ref PROPERTYKEY clave, out PROPVARIANT valor);
    }

    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, out IPropertyStore store);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PROPVARIANT valor);

    private static string? AppIdDeVentana(IntPtr hwnd)
    {
        var iid = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0) return null;
        try
        {
            var clave = new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };
            if (store.GetValue(ref clave, out var valor) != 0) return null;
            try { return valor.vt == VT_LPWSTR ? Marshal.PtrToStringUni(valor.valor) : null; }
            finally { PropVariantClear(ref valor); }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    /// <summary>Area de trabajo (sin la barra de tareas) del monitor donde esta la ventana, en pixeles reales.</summary>
    public static RECT AreaDeTrabajo(IntPtr hwnd)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref info);
        return info.rcWork;
    }

    public const int WM_CLIPBOARDUPDATE = 0x031D;
    [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    /// <summary>Saca la ventana de la capa "siempre encima" y la manda detras de las demas apps.</summary>
    public static void MandarAtras(IntPtr hwnd)
    {
        SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private static readonly IntPtr HWND_NOTOPMOST = new(-2), HWND_BOTTOM = new(1);

    public static void ReafirmarTopmost(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>
    /// Juego: pantalla completa exclusiva, o la ventana activa ocupa todo el monitor de la isla y es un juego
    /// que Windows reconoce. Video: ocupa todo el monitor pero no es un juego (navegador, reproductor...).
    /// </summary>
    public static PantallaCompleta EstadoPantallaCompleta(IntPtr propia)
    {
        if (SHQueryUserNotificationState(out int estado) == 0 && estado == QUNS_RUNNING_D3D_FULL_SCREEN)
            return PantallaCompleta.Juego;

        IntPtr activa = GetForegroundWindow();
        if (activa == IntPtr.Zero || activa == propia) return PantallaCompleta.Ninguna;

        var clase = new StringBuilder(64);
        GetClassName(activa, clase, clase.Capacity);
        if (clase.ToString() is "Progman" or "WorkerW") return PantallaCompleta.Ninguna;   // el escritorio

        IntPtr monitor = MonitorFromWindow(activa, MONITOR_DEFAULTTONEAREST);
        if (monitor != MonitorFromWindow(propia, MONITOR_DEFAULTTONEAREST)) return PantallaCompleta.Ninguna;   // otro monitor: no molesta

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return PantallaCompleta.Ninguna;

        if (!GetWindowRect(activa, out RECT r)) return PantallaCompleta.Ninguna;
        RECT m = info.rcMonitor;
        if (r.Left > m.Left || r.Top > m.Top || r.Right < m.Right || r.Bottom < m.Bottom) return PantallaCompleta.Ninguna;

        return Juegos.EsJuego(RutaDelProceso(activa)) ? PantallaCompleta.Juego : PantallaCompleta.Video;
    }

    public enum PantallaCompleta { Ninguna, Video, Juego }

    // Windows no expone "No molestar" en una API publica: se lee el estado WNF que usa el propio shell
    // (WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED: 0 = apagado, distinto de 0 = algun perfil de silencio activo)
    private const ulong WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED = 0x0D83063EA3BF1C75;
    [DllImport("ntdll.dll")]
    private static extern int NtQueryWnfStateData(ref ulong nombre, IntPtr tipo, IntPtr alcance, out uint version, byte[] datos, ref uint tamaño);

    /// <summary>true si "No molestar" esta prendido. Si no se puede leer, false (se muestran igual).</summary>
    public static bool NoMolestarActivo()
    {
        ulong nombre = WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED;
        var datos = new byte[4];
        uint tamaño = (uint)datos.Length;
        try
        {
            return NtQueryWnfStateData(ref nombre, IntPtr.Zero, IntPtr.Zero, out _, datos, ref tamaño) == 0
                   && tamaño >= 4 && BitConverter.ToInt32(datos, 0) != 0;
        }
        catch (EntryPointNotFoundException) { return false; }
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint acceso, bool heredar, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr proceso, int flags, StringBuilder ruta, ref int largo);

    /// <summary>Ruta del .exe dueño de la ventana. Acceso limitado: anda tambien con juegos que corren como admin.</summary>
    private static string? RutaDelProceso(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        IntPtr proceso = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (proceso == IntPtr.Zero) return null;
        try
        {
            var ruta = new StringBuilder(1024);
            int largo = ruta.Capacity;
            return QueryFullProcessImageName(proceso, 0, ruta, ref largo) ? ruta.ToString() : null;
        }
        finally
        {
            CloseHandle(proceso);
        }
    }
}
