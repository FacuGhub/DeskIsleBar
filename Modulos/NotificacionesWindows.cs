using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace DeskIsleBar.Modulos;

/// <summary>
/// Muestra en la isla las notificaciones que reciben las apps (Chrome, Telegram, Outlook...).
/// Fuente: la base donde Windows guarda el centro de notificaciones, abierta en SOLO LECTURA y sin
/// quedarse con el archivo. Solo cuentan las que llegan desde que arranca la isla. Con MSIX/Store se
/// cambiaria esta fuente por UserNotificationListener; el resto del modulo queda igual.
/// El contenido de las notificaciones nunca va al log.
/// </summary>
public class NotificacionesWindows
{
    internal static readonly string Base = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Notifications\wpndatabase.db");
    private static readonly Color Azul = Color.FromRgb(0x0A, 0x84, 0xFF);
    private const string Campana = "";
    private const int MaxDetalle = 140;

    private readonly Dispatcher _ui;
    private readonly Dictionary<string, string> _nombres = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _sondeo;
    private long _ultimoId = -1;   // -1 = todavia no se leyo el punto de partida
    private int _revisando;

    public event Action<Aviso>? Aviso;

    /// <summary>Interruptor de Opciones. Apagado no lee la base.</summary>
    public bool Activo { get; set; }

    /// <summary>Con "No molestar" de Windows prendido no se muestran (quedan en el centro de notificaciones).</summary>
    public bool RespetarNoMolestar { get; set; }

    /// <summary>
    /// No mostrar la notificacion de la herramienta Recortes ("Recorte copiado"): con el aviso de captura prendido
    /// la isla ya avisa por su cuenta y saldrian dos por lo mismo.
    /// </summary>
    public bool OmitirRecortes { get; set; }

    private static bool EsDeRecortes(string aumid) =>
        aumid.Contains("ScreenSketch", StringComparison.OrdinalIgnoreCase) || aumid.Contains("SnippingTool", StringComparison.OrdinalIgnoreCase);

    public NotificacionesWindows(Dispatcher ui) => _ui = ui;

    public void Iniciar()
    {
        if (!File.Exists(Base))
        {
            Log.Escribir("notificaciones: no se encontro la base de Windows, modulo apagado");
            return;
        }
        _sondeo = new Timer(_ => Revisar(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    private void Revisar()
    {
        if (Interlocked.Exchange(ref _revisando, 1) == 1) return;   // la vuelta anterior sigue
        try
        {
            // Apagado se sigue moviendo el punto de partida: al prenderlo no aparecen las viejas
            var nuevas = Leer(_ultimoId);
            bool silenciadas = !Activo || (RespetarNoMolestar && Nativo.NoMolestarActivo());
            if (_ultimoId < 0 || silenciadas) { _ultimoId = nuevas.UltimoId; return; }

            _ultimoId = nuevas.UltimoId;
            foreach (var n in nuevas.Lista)
            {
                if (OmitirRecortes && EsDeRecortes(n.App)) continue;
                // En el hilo de la ventana: el shell solo resuelve el nombre de la app desde un hilo STA
                _ui.InvokeAsync(() =>
                {
                    if (ComoAviso(n) is not { } aviso) return;
                    Log.Escribir($"notificaciones: {NombreDeApp(n.App)} (id {n.Id})");
                    Aviso?.Invoke(aviso);
                });
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            // Windows puede tener la base ocupada un instante: se reintenta en la proxima vuelta
            Log.Escribir($"notificaciones: no se pudo leer la base: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _revisando, 0);
        }
    }

    private record Notificacion(long Id, string App, byte[] Contenido);

    private static (long UltimoId, List<Notificacion> Lista) Leer(long desde)
    {
        // Pooling=False: la conexion se cierra de verdad y no deja el archivo tomado
        using var conexion = new SqliteConnection($"Data Source={Base};Mode=ReadOnly;Pooling=False");
        conexion.Open();

        long ultimo = desde;
        var lista = new List<Notificacion>();
        using var consulta = conexion.CreateCommand();
        if (desde < 0)
        {
            consulta.CommandText = "SELECT IFNULL(MAX(Id), 0) FROM Notification";
            return ((long)consulta.ExecuteScalar()!, lista);
        }

        consulta.CommandText = """
            SELECT n.Id, h.PrimaryId, n.Payload
            FROM Notification n JOIN NotificationHandler h ON h.RecordId = n.HandlerId
            WHERE n.Id > $desde AND n.Type = 'toast' AND n.PayloadType = 'Xml'
            ORDER BY n.Id
            """;
        consulta.Parameters.AddWithValue("$desde", desde);
        using var filas = consulta.ExecuteReader();
        while (filas.Read())
        {
            long id = filas.GetInt64(0);
            ultimo = Math.Max(ultimo, id);
            if (!filas.IsDBNull(2)) lista.Add(new Notificacion(id, filas.GetString(1), (byte[])filas[2]));
        }
        return (ultimo, lista);
    }

    private Aviso? ComoAviso(Notificacion n)
    {
        List<string> textos;
        try
        {
            textos = XDocument.Parse(Encoding.UTF8.GetString(n.Contenido))
                .Descendants("binding").Take(1).Descendants("text")
                .Where(t => (string?)t.Attribute("placement") != "attribution")
                .Select(t => UnaLinea(t.Value)).Where(t => t.Length > 0).ToList();
        }
        catch (System.Xml.XmlException) { return null; }
        if (textos.Count == 0) return null;   // toasts sin texto (solo imagen, progreso vacio...)

        string app = NombreDeApp(n.App);
        string detalle = string.Join(" · ", textos.Skip(1));
        detalle = detalle.Length == 0 ? app : $"{app} · {detalle}";
        if (detalle.Length > MaxDetalle) detalle = detalle[..MaxDetalle].TrimEnd() + "…";
        return new Aviso(Campana, textos[0], detalle, Azul, Carga: null, Persistente: false, Segundos: 6);
    }

    private static string UnaLinea(string texto) =>
        string.Join(' ', texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // ---------- Nombre visible de la app ----------

    /// <summary>"Chrome" → "Google Chrome", "Microsoft.Office.OUTLOOK.EXE.15" → "Outlook". Si no se puede, el id tal cual.</summary>
    private string NombreDeApp(string aumid)
    {
        if (_nombres.TryGetValue(aumid, out var nombre)) return nombre;
        nombre = NombreDesdeShell(aumid) ?? aumid;
        _nombres[aumid] = nombre;
        return nombre;
    }

    /// <summary>Nombre visible de una app por su AppUserModelId ("shell:AppsFolder"). Solo anda desde un hilo STA.</summary>
    public static string? NombreDesdeShell(string aumid)
    {
        IntPtr texto = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItem).GUID;
            if (SHCreateItemFromParsingName($@"shell:AppsFolder\{aumid}", IntPtr.Zero, ref iid, out var item) != 0) return null;
            if (item.GetDisplayName(SIGDN_NORMALDISPLAY, out texto) != 0) return null;
            string? nombre = Marshal.PtrToStringUni(texto);
            return string.IsNullOrWhiteSpace(nombre) ? null : nombre;
        }
        catch (COMException) { return null; }
        finally
        {
            if (texto != IntPtr.Zero) Marshal.FreeCoTaskMem(texto);
        }
    }

    private const uint SIGDN_NORMALDISPLAY = 0;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string ruta, IntPtr contexto, ref Guid iid, out IShellItem item);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr contexto, ref Guid bhid, ref Guid iid, out IntPtr resultado);
        [PreserveSig] int GetParent(out IShellItem padre);
        [PreserveSig] int GetDisplayName(uint tipo, out IntPtr nombre);
    }
}
