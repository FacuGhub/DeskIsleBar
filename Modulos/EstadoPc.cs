using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DeskIsleBar.Modulos;

/// <param name="Cpu">null en la primera lectura: el uso se calcula contra la anterior y todavia no hay.</param>
public record LecturaPc(double? Cpu, double Ram, double RamUsadaGb, double RamTotalGb, double? Gpu, TimeSpan Encendida);

/// <summary>
/// Uso de CPU, GPU y RAM. Sin temperaturas: en el Ryzen requieren un driver con permisos de administrador.
/// Se lee solo mientras el panel esta abierto, desde un hilo aparte (los contadores de GPU tardan).
/// </summary>
public class EstadoPc
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint Bajo, Alto;
        public readonly ulong Valor => ((ulong)Alto << 32) | Bajo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
                     ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FILETIME ocioso, out FILETIME kernel, out FILETIME usuario);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX estado);

    private const double Gb = 1024.0 * 1024 * 1024;

    private ulong _ociosoAnterior, _totalAnterior;
    private readonly Dictionary<string, PerformanceCounter> _motores3D = new();
    private DateTime _ultimaListaGpu = DateTime.MinValue;
    private bool _sinGpu;
    private bool _gpuConBase;   // los contadores devuelven 0 en su primera lectura: recien desde la segunda hay dato
    private readonly object _cerrojo = new();   // el precalentado y el panel pueden leer a la vez
    private int _lecturas;

    /// <summary>
    /// Primera lectura en segundo plano al arrancar la isla: la primera vez que un proceso usa los contadores de
    /// rendimiento tarda, y sin lectura previa no hay uso de CPU/GPU para calcular. Asi el panel abre con datos.
    /// </summary>
    public void Precalentar() => Task.Run(Leer);

    public LecturaPc Leer()
    {
        lock (_cerrojo)
        {
            var reloj = Stopwatch.StartNew();

            // CPU: proporcion de tiempo no ocioso desde la lectura anterior (el tiempo de kernel incluye el ocioso)
            GetSystemTimes(out var ocioso, out var kernel, out var usuario);
            ulong total = kernel.Valor + usuario.Valor;
            double? cpu = null;
            if (_totalAnterior != 0 && total > _totalAnterior)
                cpu = Math.Clamp(100.0 * (1 - (double)(ocioso.Valor - _ociosoAnterior) / (total - _totalAnterior)), 0, 100);
            _ociosoAnterior = ocioso.Valor;
            _totalAnterior = total;

            var memoria = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            GlobalMemoryStatusEx(ref memoria);

            var lectura = new LecturaPc(
                cpu,
                memoria.dwMemoryLoad,
                (memoria.ullTotalPhys - memoria.ullAvailPhys) / Gb,
                memoria.ullTotalPhys / Gb,
                LeerGpu(),
                TimeSpan.FromMilliseconds(Environment.TickCount64));

            if (++_lecturas <= 3)
                Log.Escribir($"pc: lectura {_lecturas} en {reloj.ElapsedMilliseconds} ms (cpu={lectura.Cpu?.ToString("0") ?? "-"} gpu={lectura.Gpu?.ToString("0") ?? "-"})");
            return lectura;
        }
    }

    /// <summary>
    /// Suma del uso de los motores 3D de la GPU (lo mismo que muestra el Administrador de tareas).
    /// Hay un contador por proceso y motor: la lista se renueva cada 10 s porque los procesos van y vienen.
    /// </summary>
    private double? LeerGpu()
    {
        if (_sinGpu) return null;
        try
        {
            if (DateTime.Now - _ultimaListaGpu > TimeSpan.FromSeconds(10))
            {
                var instancias = new PerformanceCounterCategory("GPU Engine").GetInstanceNames()
                                     .Where(n => n.EndsWith("engtype_3D", StringComparison.Ordinal)).ToHashSet();
                foreach (var vieja in _motores3D.Keys.Where(k => !instancias.Contains(k)).ToList())
                {
                    _motores3D[vieja].Dispose();
                    _motores3D.Remove(vieja);
                }
                foreach (var instancia in instancias.Where(i => !_motores3D.ContainsKey(i)))
                    _motores3D[instancia] = new PerformanceCounter("GPU Engine", "Utilization Percentage", instancia, readOnly: true);
                _ultimaListaGpu = DateTime.Now;
            }

            double suma = 0;
            foreach (var contador in _motores3D.Values)
            {
                try { suma += contador.NextValue(); }
                catch (InvalidOperationException) { }   // el proceso termino entre renovaciones
            }
            if (!_gpuConBase)
            {
                _gpuConBase = true;
                return null;   // primera lectura: los contadores siempre dan 0, no es un dato real
            }
            return Math.Clamp(suma, 0, 100);
        }
        catch (Exception ex)
        {
            Log.Escribir($"pc: no se pudo leer la GPU, se oculta: {ex.GetType().Name}: {ex.Message}");
            _sinGpu = true;
            return null;
        }
    }
}
