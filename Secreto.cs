using System.Security.Cryptography;
using System.Text;

namespace DeskIsleBar;

/// <summary>
/// Cifrado de datos sensibles (links privados de calendario) con DPAPI, atado al usuario de Windows:
/// el config copiado a otra PC u otro usuario (o visto en un backup) no sirve.
/// No protege contra un programa que corra como el mismo usuario (ese tambien podria leer el navegador).
/// </summary>
static class Secreto
{
    // NO cambiar (aunque diga "Isla", el nombre viejo del proyecto): los links ya guardados se cifraron con esto
    // y con otro valor no se podrian descifrar.
    private static readonly byte[] Entropia = "Isla.Calendario.v1"u8.ToArray();

    public static string Cifrar(string texto) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(texto), Entropia, DataProtectionScope.CurrentUser));

    /// <summary>null si no se puede descifrar (otro usuario, otra PC o dato roto).</summary>
    public static string? Descifrar(string cifrado)
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(cifrado), Entropia, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return null; }
    }

    /// <summary>
    /// Version para mostrar en pantalla sin exponer el link (que funciona como contraseña):
    /// "https://calendar.google.com/calendar/ical/abc…/basic.ics" → "calendar.google.com/…••••c.ics".
    /// </summary>
    public static string Ocultar(string url)
    {
        string host = Uri.TryCreate(url.Replace("webcal://", "https://"), UriKind.Absolute, out var uri) ? uri.Host : "link";
        string final = url.Length > 5 ? url[^5..] : "";
        return $"{host}/…••••{final}";
    }
}
