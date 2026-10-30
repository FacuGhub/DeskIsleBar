using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;
using DeskIsleBar.Modulos;

namespace DeskIsleBar;

/// <summary>
/// DeskIsleBar.exe --diagnostico: prueba cada fuente de la que lee la isla y escribe un informe (✔ / ⚠ / ✘ con el
/// detalle). Sirve para ver de un vistazo si una actualizacion de Windows rompio algo (paso con el registro de la
/// camara en 26H2) y para que un usuario lo adjunte al reportar un problema. No manda nada a ningun lado.
/// </summary>
static class Diagnostico
{
    private static string L(string es, string en) => Idioma.Actual == "es" ? es : en;

    public static async Task<string> GenerarAsync()
    {
        var informe = new StringBuilder();
        var version = typeof(Diagnostico).Assembly.GetName().Version;
        informe.AppendLine($"DeskIsleBar {version} — {L("diagnóstico", "diagnostics")} {DateTime.Now:yyyy-MM-dd HH:mm}");
        informe.AppendLine($"Windows {Environment.OSVersion.Version} ({Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", "?")})");
        informe.AppendLine();

        void Linea(string fuente, Func<(bool? Ok, string Detalle)> prueba)
        {
            string marca, detalle;
            try
            {
                var (ok, d) = prueba();
                marca = ok switch { true => "✔", false => "✘", null => "⚠" };
                detalle = d;
            }
            catch (Exception ex)
            {
                marca = "✘";
                detalle = $"{ex.GetType().Name}: {ex.Message}";
            }
            informe.AppendLine($"{marca} {fuente,-34} {detalle}");
        }

        async Task LineaAsync(string fuente, Func<Task<(bool? Ok, string Detalle)>> prueba)
        {
            try
            {
                var resultado = await prueba();
                Linea(fuente, () => resultado);
            }
            catch (Exception ex)
            {
                Linea(fuente, () => throw ex);
            }
        }

        Linea(L("Notificaciones de Windows", "Windows notifications"), () =>
        {
            using var conexion = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={NotificacionesWindows.Base};Mode=ReadOnly;Pooling=False");
            conexion.Open();
            using var consulta = conexion.CreateCommand();
            consulta.CommandText = "SELECT COUNT(*) FROM Notification WHERE Type = 'toast'";
            return (true, L($"base legible, {consulta.ExecuteScalar()} en el centro", $"database readable, {consulta.ExecuteScalar()} in the center"));
        });

        Linea(L("Estado de No molestar", "Do not disturb state"), () =>
            (true, Nativo.NoMolestarActivo() ? L("prendido", "on") : L("apagado", "off")));

        await LineaAsync(L("Música (controles multimedia)", "Music (media controls)"), async () =>
        {
            var gestor = await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var apps = gestor.GetSessions().Select(s => s.SourceAppUserModelId).ToList();
            return (true, apps.Count == 0 ? L("sin apps abiertas", "no apps open") : string.Join(", ", apps));
        });

        var audio = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        Linea(L("Salida de audio", "Audio output"), () =>
        {
            using var salida = audio.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            return (true, salida.FriendlyName);
        });

        Linea(L("Micrófono (sesiones de audio)", "Microphone (audio sessions)"), () =>
        {
            var micros = audio.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active).ToList();
            int activas = micros.Sum(m =>
            {
                m.AudioSessionManager.RefreshSessions();
                var s = m.AudioSessionManager.Sessions;
                return Enumerable.Range(0, s.Count).Count(i => !s[i].IsSystemSoundsSession &&
                    s[i].State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive);
            });
            return (micros.Count > 0 ? true : null,
                    L($"{micros.Count} micrófono(s), {activas} grabando ahora", $"{micros.Count} microphone(s), {activas} recording now"));
        });

        Linea(L("Cámara (monitor de sensores)", "Camera (sensor monitor)"), () =>
        {
            SensoresCamara.Iniciar();
            return SensoresCamara.Problema is { } problema ? (false, problema)
                 : (true, L($"funcionando, {SensoresCamara.Procesos.Count} app(s) usándola", $"working, {SensoresCamara.Procesos.Count} app(s) using it"));
        });

        Linea(L("Registro de permisos (cám./mic.)", "Permission registry (cam/mic)"), () =>
        {
            DateTime? ultimo = null;
            foreach (var recurso in new[] { "webcam", "microphone" })
            {
                using var raiz = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{recurso}");
                foreach (var clave in Claves(raiz))
                    if (clave.GetValue("LastUsedTimeStart") is long inicio && inicio > 0)
                    {
                        var fecha = DateTime.FromFileTime(inicio);
                        if (ultimo is null || fecha > ultimo) ultimo = fecha;
                    }
            }
            // Informativo: en algunas versiones de Windows deja de actualizarse; la camara y el microfono no dependen de esto
            return (null, ultimo is { } u ? L($"última anotación {u:yyyy-MM-dd} (solo apoyo)", $"last entry {u:yyyy-MM-dd} (backup only)")
                                          : L("sin anotaciones", "no entries"));
        });

        Linea(L("Controles de Xbox (XInput)", "Xbox controllers (XInput)"), () =>
            (true, XInput.LeerPrimerControl() is { } b ? L($"conectado — {XInput.Describir(b)}", $"connected — {XInput.Describir(b)}")
                                                       : L("ninguno conectado", "none connected")));

        Linea(L("Otros controles (Gaming.Input)", "Other controllers (Gaming.Input)"), () =>
        {
            var controles = Windows.Gaming.Input.RawGameController.RawGameControllers.Where(c => c.HardwareVendorId != Baterias.VendorMicrosoft).ToList();
            return (true, controles.Count == 0 ? L("ninguno conectado", "none connected") : string.Join(", ", controles.Select(c => c.DisplayName)));
        });

        await LineaAsync(L("Batería Bluetooth", "Bluetooth battery"), async () =>
        {
            const string propiedad = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
            var dispositivos = await Windows.Devices.Enumeration.Pnp.PnpObject.FindAllAsync(
                Windows.Devices.Enumeration.Pnp.PnpObjectType.Device, [propiedad, "System.ItemNameDisplay"]);
            // Sin el nombre del dispositivo: suele traer el de la persona ("Auriculares de Juan")
            var conBateria = dispositivos.Where(d => d.Properties.TryGetValue(propiedad, out var v) && v is byte)
                                         .Select(d => $"{d.Properties[propiedad]}%").ToList();
            return (true, conBateria.Count == 0 ? L("ningún dispositivo informa batería", "no device reports battery") : string.Join(", ", conBateria));
        });

        Linea(L("Batería de la PC", "PC battery"), () =>
        {
            if (!GetSystemPowerStatus(out var e)) return (false, L("Windows no informa el estado de energía", "Windows doesn't report the power state"));
            if ((e.BatteryFlag & 128) != 0) return (true, L("sin batería (PC de escritorio)", "no battery (desktop PC)"));
            return (true, $"{e.BatteryLifePercent}%, {(e.ACLineStatus == 1 ? L("enchufada", "plugged in") : L("con batería", "on battery"))}");
        });

        Linea("Internet", () =>
        {
            var perfil = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            var nivel = perfil?.GetNetworkConnectivityLevel();
            return (nivel == Windows.Networking.Connectivity.NetworkConnectivityLevel.InternetAccess ? true : null,
                    nivel?.ToString() ?? L("sin red", "no network"));   // sin el nombre de la red: puede decir donde esta la PC
        });

        Linea(L("Juegos reconocidos", "Recognized games"), () =>
            (Juegos.Cantidad > 0 ? true : null, L($"{Juegos.Cantidad} (lista de la Game Bar)", $"{Juegos.Cantidad} (Game Bar list)")));

        Linea(L("Pantalla completa", "Full screen"), () => (true, Nativo.EstadoPantallaCompleta(IntPtr.Zero).ToString()));

        informe.AppendLine();
        informe.AppendLine(L("✔ funciona · ⚠ funciona pero conviene mirarlo · ✘ no funciona",
                             "✔ works · ⚠ works but worth a look · ✘ not working"));

        // Se pega en issues publicos: un mensaje de error puede traer una ruta con el usuario o el nombre de la PC
        string texto = informe.ToString();
        foreach (var (dato, reemplazo) in new[] { (Environment.UserName, "<usuario>"), (Environment.MachineName, "<pc>") })
            if (dato.Length >= 2) texto = texto.Replace(dato, reemplazo, StringComparison.OrdinalIgnoreCase);
        return texto;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct EstadoEnergia { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out EstadoEnergia estado);

    private static IEnumerable<RegistryKey> Claves(RegistryKey? raiz)
    {
        if (raiz is null) yield break;
        foreach (var nombre in raiz.GetSubKeyNames())
        {
            using var clave = raiz.OpenSubKey(nombre);
            if (clave is null) continue;
            yield return clave;
            foreach (var sub in Claves(clave)) yield return sub;
        }
    }

    /// <summary>Genera el informe, lo guarda junto a los datos del usuario y lo abre en el Bloc de notas.</summary>
    public static async Task EjecutarAsync()
    {
        string texto = await GenerarAsync();
        string archivo = Rutas.Archivo("diagnostico.txt");
        Directory.CreateDirectory(Rutas.Datos);
        File.WriteAllText(archivo, texto, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{archivo}\"") { UseShellExecute = true });
    }
}
