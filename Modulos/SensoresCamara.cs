using System.Runtime.InteropServices;

namespace DeskIsleBar.Modulos;

/// <summary>
/// Que procesos estan usando una camara, segun el monitor de actividad de sensores de Media Foundation
/// (Windows 10 1703+, sin permisos especiales). Windows avisa cada cambio; aca se guarda la foto del momento.
/// El registro de permisos (CapabilityAccessManager) deja de actualizarse en algunas versiones de Windows,
/// asi que esta es la fuente principal para la camara.
/// GUIDs y orden de los metodos: sacados de los metadatos oficiales (Windows.Win32.winmd).
/// </summary>
static class SensoresCamara
{
    private static IMFSensorActivityMonitor? _monitor;
    private static Receptor? _receptor;   // referencias vivas: si el GC las junta, Windows llama a un objeto muerto

    /// <summary>PIDs de los procesos que estan recibiendo imagen de alguna camara ahora.</summary>
    public static IReadOnlyCollection<int> Procesos => _receptor?.Procesos ?? (IReadOnlyCollection<int>)[];

    /// <summary>El monitor de sensores arranco bien (para el diagnostico); si no, el motivo.</summary>
    public static string? Problema { get; private set; } = "no iniciado";

    public static void Iniciar()
    {
        if (_monitor is not null) return;
        try
        {
            // Media Foundation tiene que estar iniciado (si no: MF_E_PLATFORM_NOT_INITIALIZED, 0xC00D3E85)
            int hr = MFStartup(MF_VERSION, MFSTARTUP_FULL);
            if (hr != 0) { Fallo($"no se pudo iniciar Media Foundation (0x{hr:X8})"); return; }

            _receptor = new Receptor();
            hr = MFCreateSensorActivityMonitor(_receptor, out _monitor);
            if (hr != 0) { _monitor = null; Fallo($"no se pudo crear el monitor de sensores (0x{hr:X8})"); return; }
            hr = _monitor.Start();
            if (hr != 0) { Fallo($"no se pudo iniciar el monitor de sensores (0x{hr:X8})"); return; }
            Problema = null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException)
        {
            Fallo($"monitor de sensores no disponible: {ex.Message}");
        }
    }

    private static void Fallo(string motivo)
    {
        Problema = motivo;
        Log.Escribir($"camara: {motivo}");
    }

    [ComVisible(true)]
    private sealed class Receptor : IMFSensorActivitiesReportCallback
    {
        private volatile HashSet<int> _procesos = [];
        public IReadOnlyCollection<int> Procesos => _procesos;

        // Llega en un hilo de Media Foundation: se arma un conjunto nuevo y se reemplaza de una
        public int OnActivitiesReport(IMFSensorActivitiesReport informe)
        {
            var procesos = new HashSet<int>();
            try
            {
                informe.GetCount(out uint sensores);
                for (uint i = 0; i < sensores; i++)
                {
                    if (informe.GetActivityReport(i, out var sensor) != 0) continue;
                    sensor.GetProcessCount(out uint cantidad);
                    for (uint j = 0; j < cantidad; j++)
                    {
                        if (sensor.GetProcessActivity(j, out var actividad) != 0) continue;
                        actividad.GetStreamingState(out int transmitiendo);
                        if (transmitiendo != 0 && actividad.GetProcessId(out uint pid) == 0) procesos.Add((int)pid);
                    }
                }
            }
            catch (COMException) { return 0; }   // informe a medias: se queda la foto anterior
            _procesos = procesos;
            return 0;
        }
    }

    private const uint MF_VERSION = 0x00020070, MFSTARTUP_FULL = 0;
    [DllImport("mfplat.dll")] private static extern int MFStartup(uint version, uint flags);

    [DllImport("mfsensorgroup.dll")]
    private static extern int MFCreateSensorActivityMonitor(IMFSensorActivitiesReportCallback callback, out IMFSensorActivityMonitor monitor);

    [ComImport, Guid("D0CEF145-B3F4-4340-A2E5-7A5080CA05CB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSensorActivityMonitor
    {
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
    }

    [ComImport, Guid("DE5072EE-DBE3-46DC-8A87-B6F631194751"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSensorActivitiesReportCallback
    {
        [PreserveSig] int OnActivitiesReport(IMFSensorActivitiesReport informe);
    }

    [ComImport, Guid("683F7A5E-4A19-43CD-B1A9-DBF4AB3F7777"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSensorActivitiesReport
    {
        [PreserveSig] int GetCount(out uint cantidad);
        [PreserveSig] int GetActivityReport(uint indice, out IMFSensorActivityReport sensor);
        [PreserveSig] int GetActivityReportByDeviceName([MarshalAs(UnmanagedType.LPWStr)] string nombre, out IMFSensorActivityReport sensor);
    }

    [ComImport, Guid("3E8C4BE1-A8C2-4528-90DE-2851BDE5FEAD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSensorActivityReport
    {
        [PreserveSig] int GetFriendlyName(IntPtr nombre, uint largo, out uint escritos);
        [PreserveSig] int GetSymbolicLink(IntPtr enlace, uint largo, out uint escritos);
        [PreserveSig] int GetProcessCount(out uint cantidad);
        [PreserveSig] int GetProcessActivity(uint indice, out IMFSensorProcessActivity actividad);
    }

    [ComImport, Guid("39DC7F4A-B141-4719-813C-A7F46162A2B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSensorProcessActivity
    {
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int GetStreamingState(out int transmitiendo);
        [PreserveSig] int GetStreamingMode(out int modo);
        [PreserveSig] int GetReportTime(out long cuando);
    }
}
