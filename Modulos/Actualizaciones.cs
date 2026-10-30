using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace DeskIsleBar.Modulos;

public record VersionNueva(Version Version, string Url, string Sha256);

/// <summary>
/// Busca en GitHub si hay una version nueva (al arrancar, una vez por dia y a pedido desde Opciones) y la instala.
/// La consulta solo pide los datos del ultimo release publico: no manda nada del usuario (GitHub ve la IP, como
/// cualquier pagina). El instalador se baja solo desde los releases del repo oficial, se verifica con el SHA-256
/// que publica GitHub y se ejecuta en modo silencioso; al terminar vuelve a abrir la isla.
/// </summary>
public static class Actualizaciones
{
    private const string Api = "https://api.github.com/repos/FacuGhub/DeskIsleBar/releases/latest";
    private const string PrefijoDescarga = "https://github.com/FacuGhub/DeskIsleBar/releases/download/";
    private const long MaxInstalador = 300L * 1024 * 1024;

    private static readonly HttpClient Http = CrearCliente();

    private static HttpClient CrearCliente()
    {
        var cliente = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        cliente.DefaultRequestHeaders.UserAgent.ParseAdd("DeskIsleBar");   // la API de GitHub lo exige
        return cliente;
    }

    /// <summary>La version que esta corriendo (mayor.menor.revision).</summary>
    public static Version Actual
    {
        get
        {
            var v = typeof(Actualizaciones).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>La version nueva si hay una; null si ya esta al dia. Tira HttpRequestException si no hay conexion.</summary>
    public static async Task<VersionNueva?> BuscarAsync()
    {
        using var corte = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var respuesta = await Http.GetAsync(Api, HttpCompletionOption.ResponseHeadersRead, corte.Token);
        respuesta.EnsureSuccessStatusCode();
        if (respuesta.Content.Headers.ContentLength > 1024 * 1024) return null;
        using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(corte.Token));
        var release = json.RootElement;

        if (release.TryGetProperty("draft", out var borrador) && borrador.GetBoolean()) return null;
        if (release.TryGetProperty("prerelease", out var previa) && previa.GetBoolean()) return null;
        string etiqueta = release.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(etiqueta.TrimStart('v', 'V'), out var version)) return null;
        version = new Version(version.Major, version.Minor, Math.Max(0, version.Build));
        if (version <= Actual) return null;

        foreach (var archivo in release.GetProperty("assets").EnumerateArray())
        {
            string nombre = archivo.GetProperty("name").GetString() ?? "";
            string url = archivo.GetProperty("browser_download_url").GetString() ?? "";
            string digest = archivo.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
            // Solo el instalador, solo del repo oficial y con su huella publicada
            if (nombre.StartsWith("DeskIsleBar-Setup-", StringComparison.OrdinalIgnoreCase) && nombre.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                url.StartsWith(PrefijoDescarga, StringComparison.Ordinal) && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                return new VersionNueva(version, url, digest["sha256:".Length..].ToLowerInvariant());
        }
        Log.Escribir($"actualizaciones: la {version} no tiene un instalador verificable");
        return null;
    }

    /// <summary>
    /// Baja el instalador, lo verifica y lo lanza en silencio. Devuelve true si quedo lanzado: la isla tiene que
    /// cerrarse enseguida (el instalador espera a que se cierre y despues la vuelve a abrir).
    /// </summary>
    public static async Task<bool> InstalarAsync(VersionNueva nueva)
    {
        string carpeta = Path.Combine(Path.GetTempPath(), "DeskIsleBar-actualizacion");
        Directory.CreateDirectory(carpeta);
        string instalador = Path.Combine(carpeta, $"DeskIsleBar-Setup-{nueva.Version}.exe");
        try
        {
            using (var respuesta = await Http.GetAsync(nueva.Url, HttpCompletionOption.ResponseHeadersRead))
            {
                respuesta.EnsureSuccessStatusCode();
                if (respuesta.Content.Headers.ContentLength > MaxInstalador) throw new InvalidDataException("demasiado grande");
                await using var origen = await respuesta.Content.ReadAsStreamAsync();
                await using var destino = File.Create(instalador);
                var buffer = new byte[81920];
                long total = 0;
                int leidos;
                while ((leidos = await origen.ReadAsync(buffer)) > 0)
                {
                    total += leidos;
                    if (total > MaxInstalador) throw new InvalidDataException("demasiado grande");
                    await destino.WriteAsync(buffer.AsMemory(0, leidos));
                }
            }

            string huella;
            await using (var archivo = File.OpenRead(instalador))
                huella = Convert.ToHexStringLower(await SHA256.HashDataAsync(archivo));
            if (huella != nueva.Sha256)
            {
                Log.Escribir("actualizaciones: el instalador bajado no coincide con la huella publicada, se descarta");
                File.Delete(instalador);
                return false;
            }

            // El instalador no puede correr con la isla abierta (AppMutex): se lanza con 2 s de demora, oculto,
            // y la isla se cierra mientras tanto. /actualizar=1 hace que al terminar la vuelva a abrir.
            // La ruta va en una variable de entorno: cmd la expande una sola vez y no interpreta lo que traiga
            // (ej. un % o un & en la carpeta del usuario).
            var lanzador = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = "/d /c ping -n 3 127.0.0.1 >nul & \"%DESKISLEBAR_INSTALADOR%\" /SILENT /SUPPRESSMSGBOXES /NORESTART /actualizar=1",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            lanzador.Environment["DESKISLEBAR_INSTALADOR"] = instalador;
            System.Diagnostics.Process.Start(lanzador);
            Log.Escribir($"actualizaciones: instalando la {nueva.Version}");
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException
                                         or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Escribir($"actualizaciones: no se pudo instalar: {ex.GetType().Name}");
            try { File.Delete(instalador); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return false;
        }
    }
}
