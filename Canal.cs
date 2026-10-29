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

    public Aviso ComoAviso()
    {
        // El icono puede ser un nombre del diccionario o un codigo de Segoe Fluent Icons ("E7BA")
        string icono = Iconos.GetValueOrDefault(Icono ?? "info") ??
                       (Icono is { Length: 4 } && int.TryParse(Icono, System.Globalization.NumberStyles.HexNumber, null, out int codigo)
                           ? char.ConvertFromUtf32(codigo) : Iconos["info"]);
        MediaColor color = Colores.GetValueOrDefault(Color ?? "", Colors.White);
        return new Aviso(icono, Titulo, Detalle ?? "", color, Carga: null, Persistente: Fijo);
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
/// CurrentUserOnly: solo procesos del mismo usuario pueden escribir.
/// </summary>
static class Canal
{
    private const string Nombre = "DeskIsleBar";

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
                    using var lector = new StreamReader(servidor);
                    string json = await lector.ReadToEndAsync();

                    if (JsonSerializer.Deserialize<PedidoAviso>(json) is { Titulo.Length: > 0 } pedido)
                    {
                        Log.Escribir($"canal: aviso \"{pedido.Titulo}\"");
                        _ = ui.InvokeAsync(() => alRecibir(pedido.ComoAviso()));
                    }
                }
                catch (Exception ex)
                {
                    Log.Escribir($"canal: error: {ex.Message}");
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
            Log.Escribir($"canal: no se pudo enviar \"{pedido.Titulo}\": {ex.Message}");
            return false;
        }
    }
}
