using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using System.Windows.Threading;

namespace DeskIsleBar;

/// <summary>
/// Lo que manda un script con: DeskIsleBar.exe --aviso "Titulo" "Detalle" [--icono X] [--color X] [--fijo]
/// </summary>
public record PedidoAviso(string Titulo, string? Detalle, string? Icono, string? Color, bool Fijo)
{
    private static readonly Dictionary<string, string> Iconos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["info"] = "", ["alerta"] = "", ["candado"] = "",
        ["joystick"] = "", ["campana"] = "", ["red"] = "",
    };

    private static readonly Dictionary<string, MediaColor> Colores = new(StringComparer.OrdinalIgnoreCase)
    {
        ["naranja"] = MediaColor.FromRgb(0xFF, 0x9F, 0x0A), ["rojo"] = MediaColor.FromRgb(0xFF, 0x45, 0x3A),
        ["verde"] = MediaColor.FromRgb(0x30, 0xD1, 0x58), ["azul"] = MediaColor.FromRgb(0x0A, 0x84, 0xFF),
    };

    private const int MaxTitulo = 120, MaxDetalle = 300;

    private static string Recortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..(maximo - 1)] + "…";

    public Aviso ComoAviso()
    {
        // El icono puede ser un nombre del diccionario o un codigo de Segoe Fluent Icons ("E7BA", zona de uso privado)
        string icono = Iconos.GetValueOrDefault(Icono ?? "info") ??
                       (Icono is { Length: 4 } && int.TryParse(Icono, System.Globalization.NumberStyles.HexNumber, null, out int codigo)
                        && codigo is >= 0xE000 and <= 0xF8FF
                           ? char.ConvertFromUtf32(codigo) : Iconos["info"]);
        MediaColor color = Colores.GetValueOrDefault(Color ?? "", Colors.White);
        return new Aviso(icono, Recortar(Titulo, MaxTitulo), Recortar(Detalle ?? "", MaxDetalle), color, Carga: null, Persistente: Fijo);
    }

    /// <summary>null si los argumentos no traen un --aviso.</summary>
    public static PedidoAviso? DesdeArgumentos(string[] args)
    {
        int i = Array.FindIndex(args, a => a.Equals("--aviso", StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Length) return null;

        string titulo = args[i + 1];
        string? detalle = i + 2 < args.Length && !args[i + 2].StartsWith("--") ? args[i + 2] : null;
        string? Opcion(string nombre)
        {
            int j = Array.FindIndex(args, a => a.Equals(nombre, StringComparison.OrdinalIgnoreCase));
            return j >= 0 && j + 1 < args.Length ? args[j + 1] : null;
        }
        bool fijo = args.Any(a => a.Equals("--fijo", StringComparison.OrdinalIgnoreCase));
        return new PedidoAviso(titulo, detalle, Opcion("--icono"), Opcion("--color"), fijo);
    }
}

/// <summary>
/// Pipe local por el que otras instancias de DeskIsleBar.exe (lanzadas por scripts) le pasan avisos a la que esta corriendo.
/// CurrentUserOnly: solo procesos del mismo usuario pueden escribir, y el que envia verifica que la isla que escucha
/// sea del mismo usuario. El nombre lleva la sesion de Windows porque los pipes son de toda la PC: sin eso, con dos
/// usuarios conectados a la vez (cambio rapido de usuario) la isla del segundo no podria abrir el suyo.
/// </summary>
static class Canal
{
    private static readonly string Nombre = $"DeskIsleBar-{System.Diagnostics.Process.GetCurrentProcess().SessionId}";

    /// <summary>Un aviso es un JSON chico: un cliente que manda de mas o no cierra no traba el canal.</summary>
    private const int MaxBytes = 16 * 1024;
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(5);

    public static void Escuchar(Dispatcher ui, Action<Aviso> alRecibir)
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await using var servidor = new NamedPipeServerStream(Nombre, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await servidor.WaitForConnectionAsync();
                    using var corte = new CancellationTokenSource(EsperaMaxima);
                    var buffer = new byte[MaxBytes + 1];
                    int leidos = 0, n;
                    while (leidos < buffer.Length && (n = await servidor.ReadAsync(buffer.AsMemory(leidos), corte.Token)) > 0) leidos += n;
                    if (leidos > MaxBytes)
                    {
                        Log.Escribir("canal: aviso descartado (demasiado largo)");
                        continue;
                    }

                    if (JsonSerializer.Deserialize<PedidoAviso>(buffer.AsSpan(0, leidos)) is { Titulo.Length: > 0 } pedido)
                    {
                        Log.Escribir("canal: aviso recibido");   // sin el texto: puede ser privado
                        _ = ui.InvokeAsync(() => alRecibir(pedido.ComoAviso()));
                    }
                }
                catch (Exception ex)
                {
                    Log.Escribir($"canal: error: {ex.GetType().Name}");
                    await Task.Delay(1000);
                }
            }
        });
    }

    public static bool Enviar(PedidoAviso pedido)
    {
        try
        {
            using var cliente = new NamedPipeClientStream(".", Nombre, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            cliente.Connect(3000);
            using var escritor = new StreamWriter(cliente);
            escritor.Write(JsonSerializer.Serialize(pedido));
            return true;
        }
        catch (Exception ex)
        {
            Log.Escribir($"canal: no se pudo enviar el aviso: {ex.GetType().Name}");
            return false;
        }
    }
}
