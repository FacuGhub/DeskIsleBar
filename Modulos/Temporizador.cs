using System.Windows.Media;
using System.Windows.Threading;

namespace DeskIsleBar.Modulos;

/// <summary>
/// Cuenta regresiva simple o Pomodoro (estudio + descanso encadenado).
/// Mientras corre, la isla en reposo muestra el tiempo que falta.
/// </summary>
public class Temporizador
{
    public enum Fase { Simple, Estudio, Descanso }

    private static readonly Color Naranja = Color.FromRgb(0xFF, 0x9F, 0x0A);
    private static readonly Color Verde = Color.FromRgb(0x30, 0xD1, 0x58);
    private static readonly Color Azul = Color.FromRgb(0x0A, 0x84, 0xFF);

    private readonly DispatcherTimer _tic = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _fin;
    private TimeSpan? _descanso;

    public Fase FaseActual { get; private set; }
    public bool Activo => _tic.IsEnabled;
    /// <summary>Corriendo el bloque de estudio de un Pomodoro (para el modo foco).</summary>
    public bool Estudiando => Activo && FaseActual == Fase.Estudio;
    public TimeSpan Restante => _fin > DateTime.Now ? _fin - DateTime.Now : TimeSpan.Zero;
    public Color Color => FaseActual switch { Fase.Estudio => Naranja, Fase.Descanso => Verde, _ => Azul };

    /// <summary>Cada segundo, y al empezar o cancelar.</summary>
    public event Action? Cambio;
    public event Action<Aviso>? Aviso;

    public Temporizador() => _tic.Tick += (_, _) => AlTic();

    public void Iniciar(TimeSpan duracion) => Arrancar(Fase.Simple, duracion, descanso: null);
    public void IniciarPomodoro(TimeSpan estudio, TimeSpan descanso) => Arrancar(Fase.Estudio, estudio, descanso);

    public void Cancelar()
    {
        _tic.Stop();
        Cambio?.Invoke();
    }

    private void Arrancar(Fase fase, TimeSpan duracion, TimeSpan? descanso)
    {
        FaseActual = fase;
        _descanso = descanso;
        _fin = DateTime.Now + duracion;
        _tic.Start();
        Cambio?.Invoke();
    }

    private void AlTic()
    {
        if (DateTime.Now < _fin)
        {
            Cambio?.Invoke();
            return;
        }

        switch (FaseActual)
        {
            case Fase.Estudio when _descanso is { } descanso:
                Avisar(Idioma.T("Tmp.FinEstudio"), Idioma.T("Tmp.Descanso", descanso.TotalMinutes.ToString("0")), Verde);
                Arrancar(Fase.Descanso, descanso, descanso: null);
                break;
            case Fase.Descanso:
                _tic.Stop();
                Avisar(Idioma.T("Tmp.FinDescanso"), Idioma.T("Tmp.AEstudiar"), Naranja);
                Cambio?.Invoke();
                break;
            default:
                _tic.Stop();
                Avisar(Idioma.T("Tmp.Fin"), "", Azul);
                Cambio?.Invoke();
                break;
        }
    }

    private void Avisar(string titulo, string detalle, Color color) =>
        Aviso?.Invoke(new Aviso("", titulo, detalle, color, Carga: null, Persistente: true));
}
