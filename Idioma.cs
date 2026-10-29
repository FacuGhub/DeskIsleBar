using System.Globalization;
using System.Windows;

namespace DeskIsleBar;

/// <summary>
/// Idioma de la interfaz. Los textos viven en Idiomas\es.xaml y en.xaml (un diccionario por idioma, mismas claves):
/// el XAML los toma con {DynamicResource clave}, que cambia al instante, y el codigo con <see cref="T(string)"/>.
/// Para sumar un idioma: copiar en.xaml con el codigo nuevo, traducirlo y agregarlo a <see cref="Disponibles"/>.
/// </summary>
static class Idioma
{
    public static readonly (string Codigo, string Nombre)[] Disponibles = [("es", "Español"), ("en", "English")];
    private const string PorDefecto = "en";

    private static ResourceDictionary? _textos;

    /// <summary>Codigo del idioma en uso ("es", "en").</summary>
    public static string Actual { get; private set; } = PorDefecto;
    /// <summary>Para fechas y numeros: la de Windows si es del mismo idioma (respeta el pais), si no una estandar.</summary>
    public static CultureInfo Cultura { get; private set; } = CultureInfo.GetCultureInfo("en-US");
    public static event Action? Cambio;

    /// <param name="preferencia">"auto" = el de Windows si esta disponible, si no ingles; o un codigo.</param>
    public static void Aplicar(string preferencia)
    {
        string windows = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        string codigo = preferencia == "auto" ? windows : preferencia;
        if (!Disponibles.Any(d => d.Codigo == codigo)) codigo = PorDefecto;

        var textos = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Idiomas/{codigo}.xaml") };
        var recursos = Application.Current.Resources.MergedDictionaries;
        if (_textos is not null) recursos.Remove(_textos);
        recursos.Add(textos);
        _textos = textos;

        Actual = codigo;
        Cultura = CultureInfo.CurrentCulture.TwoLetterISOLanguageName == codigo ? CultureInfo.CurrentCulture
                : CultureInfo.GetCultureInfo(codigo == "es" ? "es-AR" : "en-US");
        Cambio?.Invoke();
    }

    /// <summary>Texto de la clave en el idioma actual; si falta, la clave misma (asi se nota en pantalla).</summary>
    public static string T(string clave) => Application.Current?.TryFindResource(clave) as string ?? clave;

    public static string T(string clave, params object[] valores) => string.Format(Cultura, T(clave), valores);
}
