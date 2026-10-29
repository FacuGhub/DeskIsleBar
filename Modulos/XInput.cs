using System.Runtime.InteropServices;

namespace DeskIsleBar.Modulos;

/// <summary>Lectura del joystick Xbox por XInput (compartida por la bateria y la deteccion de conexion).</summary>
static class XInput
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Bateria { public byte Tipo; public byte Nivel; }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetBatteryInformation(uint usuario, byte dispositivo, out Bateria info);

    public const byte TIPO_DESCONECTADO = 0, TIPO_CABLE = 1, TIPO_DESCONOCIDO = 255;
    public const byte NIVEL_VACIO = 0, NIVEL_BAJO = 1, NIVEL_MEDIO = 2, NIVEL_LLENO = 3;

    /// <summary>Bateria del primer control conectado en los 4 slots; null si no hay ninguno.</summary>
    public static Bateria? LeerPrimerControl()
    {
        for (uint i = 0; i < 4; i++)
        {
            if (XInputGetBatteryInformation(i, 0, out var info) == 0 && info.Tipo != TIPO_DESCONECTADO)
                return info;
        }
        return null;
    }

    public static string Describir(Bateria b) => b.Tipo switch
    {
        TIPO_CABLE => Idioma.T("XI.Cable"),
        TIPO_DESCONOCIDO => "",
        _ => b.Nivel switch
        {
            NIVEL_LLENO => Idioma.T("XI.Llena"),
            NIVEL_MEDIO => Idioma.T("XI.Media"),
            NIVEL_BAJO => Idioma.T("XI.Baja"),
            _ => Idioma.T("XI.CasiVacia"),
        },
    };
}
