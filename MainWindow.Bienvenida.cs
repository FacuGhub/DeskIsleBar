using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DeskIsleBar;

/// <summary>
/// Bienvenida: la primera vez que se abre la isla (o desde Opciones → ?) enseña lo que no se descubre solo.
/// Se recorre con el mismo gesto que enseña (arrastrar hacia un costado) y los puntitos marcan el paso.
/// Los pasos de accion (cambiar de panel, mover, volumen) esperan a que se hagan; los demas tienen "Siguiente".
/// Al terminar o saltearla queda anotada en el config: no vuelve a aparecer, tampoco al actualizar.
/// </summary>
public partial class MainWindow
{
    private enum PasoBienvenida { Hola, Paneles, Mover, Volumen, Calendario, Clima, Opciones }

    private static readonly PasoBienvenida[] PasosBienvenida = Enum.GetValues<PasoBienvenida>();

    private PasoBienvenida _pasoBienvenida;
    private readonly HashSet<PasoBienvenida> _pasosHechos = [];
    private bool _bienvenidaPendiente;   // se muestra en cuanto la isla este a la vista y en reposo (ver Vigilar)
    // Hecha la accion pasa sola al siguiente, con un respiro para ver el efecto (ej. la barra de volumen)
    private readonly DispatcherTimer _avanceBienvenida = new() { Interval = TimeSpan.FromMilliseconds(400) };

    private static bool EsDeAccion(PasoBienvenida paso) =>
        paso is PasoBienvenida.Paneles or PasoBienvenida.Mover or PasoBienvenida.Volumen;

    private bool PasoBienvenidaHecho => !EsDeAccion(_pasoBienvenida) || _pasosHechos.Contains(_pasoBienvenida);

    private void PrepararBienvenida()
    {
        _bienvenidaPendiente = !_config.BienvenidaVista && !Demo.Activo;
        _avanceBienvenida.Tick += (_, _) =>
        {
            _avanceBienvenida.Stop();
            if (_modo == Modo.Bienvenida && PasoBienvenidaHecho) AvanzarBienvenida(+1, desde: 40);
        };

        foreach (var (codigo, nombre) in Idioma.Disponibles)
        {
            var chip = new RadioButton
            {
                Tag = codigo,
                GroupName = "IdiomaBienvenida",
                Style = (Style)FindResource("Chip"),
                Content = nombre,
                IsChecked = codigo == Idioma.Actual,
                Margin = new Thickness(0, 0, 6, 0),
            };
            chip.Click += IdiomaBienvenida_Click;
            DemoHola.Children.Add(chip);
        }
    }

    /// <summary>Llamado por Vigilar cuando la isla esta a la vista: si la bienvenida quedo pendiente, ahora.</summary>
    private void IntentarBienvenida()
    {
        if (!_bienvenidaPendiente || _modo != Modo.Reposo || !IsVisible || _conVideo) return;
        _bienvenidaPendiente = false;
        MostrarBienvenida();
    }

    private void MostrarBienvenida()
    {
        _pasoBienvenida = PasoBienvenida.Hola;
        _pasosHechos.Clear();
        Expandir(Modo.Bienvenida, deslizarDesde: _modo == Modo.Reposo ? 0 : 40);
        Log.Escribir("bienvenida: empieza");
    }

    private void VerBienvenida_Click(object sender, RoutedEventArgs e) => MostrarBienvenida();

    /// <summary>Arma el paso actual (lo llama Expandir).</summary>
    private void MostrarPasoBienvenida()
    {
        var paso = _pasoBienvenida;
        TituloBienvenida.SetResourceReference(TextBlock.TextProperty, $"Bien.{paso}.Titulo");
        TextoBienvenida.SetResourceReference(TextBlock.TextProperty, $"Bien.{paso}.Texto");

        FrameworkElement demo = paso switch
        {
            PasoBienvenida.Hola => DemoHola,
            PasoBienvenida.Paneles => DemoPaneles,
            PasoBienvenida.Mover => DemoMover,
            PasoBienvenida.Volumen => DemoVolumen,
            PasoBienvenida.Calendario => DemoCalendario,
            PasoBienvenida.Clima => DemoClima,
            _ => DemoOpciones,
        };
        foreach (var d in new FrameworkElement[] { DemoHola, DemoPaneles, DemoMover, DemoVolumen, DemoCalendario, DemoClima, DemoOpciones })
            d.Visibility = d == demo ? Visibility.Visible : Visibility.Collapsed;

        foreach (RadioButton chip in DemoHola.Children)
            chip.IsChecked = (string)chip.Tag == Idioma.Actual;
        if (paso == PasoBienvenida.Clima) CampoCiudadBienvenida.Text = Demo.Activo ? Demo.Ciudad : _config.Ciudad;
        if (paso == PasoBienvenida.Volumen && !_pasosHechos.Contains(paso))
        {
            EscalaVolumenDemo.ScaleX = 0;
            PorcentajeVolumenDemo.Text = "";
        }

        SaltarBienvenida.Visibility = paso == PasoBienvenida.Opciones ? Visibility.Collapsed : Visibility.Visible;
        ActualizarPieBienvenida();
        AnimarDemo(paso);
    }

    /// <summary>Abajo: la consigna (azul) mientras falta la accion, y el boton (si ya se hizo o no hay accion).</summary>
    private void ActualizarPieBienvenida()
    {
        var paso = _pasoBienvenida;
        if (EsDeAccion(paso))
        {
            bool hecho = _pasosHechos.Contains(paso);
            if (hecho) PistaBienvenida.Text = "";
            else PistaBienvenida.SetResourceReference(TextBlock.TextProperty, $"Bien.{paso}.Pista");
            SiguienteBienvenida.Visibility = hecho ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            PistaBienvenida.Text = "";
            SiguienteBienvenida.Visibility = Visibility.Visible;
        }
        TextoSiguienteBienvenida.SetResourceReference(TextBlock.TextProperty, paso switch
        {
            PasoBienvenida.Hola => "Bien.Empezar",
            PasoBienvenida.Opciones => "Bien.VerOpciones",
            _ => "Bien.Siguiente",
        });
    }

    /// <summary>Se hizo la accion que pedia el paso: pasa solo al siguiente.</summary>
    private void CompletarPasoBienvenida(PasoBienvenida paso)
    {
        if (_modo != Modo.Bienvenida || _pasoBienvenida != paso || !_pasosHechos.Add(paso)) return;
        ActualizarPieBienvenida();
        _avanceBienvenida.Stop();
        _avanceBienvenida.Start();
    }

    private void AvanzarBienvenida(int direccion, double desde)
    {
        int destino = (int)_pasoBienvenida + direccion;
        if (destino < 0 || destino >= PasosBienvenida.Length) return;
        if (direccion > 0 && !PasoBienvenidaHecho) return;   // falta la accion: la consigna ya lo dice

        _avanceBienvenida.Stop();
        if (_pasoBienvenida == PasoBienvenida.Clima) GuardarCiudadBienvenida();
        _pasoBienvenida = PasosBienvenida[destino];
        Expandir(Modo.Bienvenida, deslizarDesde: desde);
    }

    /// <summary>El gesto de cambiar de panel, dentro de la bienvenida: en su paso, es justo lo que se pedia.</summary>
    private void PasarBienvenida(int direccion)
    {
        double desde = direccion > 0 ? 40 : -40;
        if (_pasoBienvenida == PasoBienvenida.Paneles && !_pasosHechos.Contains(PasoBienvenida.Paneles))
        {
            _pasosHechos.Add(PasoBienvenida.Paneles);
            Log.Escribir("bienvenida: cambio de panel hecho");
            AvanzarBienvenida(+1, desde);   // el panel nuevo entrando ya es la respuesta: sin esperar
            return;
        }
        AvanzarBienvenida(direccion, desde);
    }

    /// <summary>
    /// Paso Mover: apretar en el panel (no en un boton) lo arrastra entero, como a la pildora cerrada.
    /// Devuelve false si no corresponde (otro paso o se apreto un boton).
    /// </summary>
    private bool MoverEnBienvenida(DependencyObject origen)
    {
        if (_modo != Modo.Bienvenida || _pasoBienvenida != PasoBienvenida.Mover) return false;
        for (var d = origen; d is not null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is ButtonBase) return false;

        double antesX = Left, antesY = Top;
        DragMove();   // vuelve al soltar
        bool movio = Math.Abs(Left - antesX) + Math.Abs(Top - antesY) > 30;
        Reorientar();
        if (movio)
        {
            Log.Escribir("bienvenida: isla movida");
            CompletarPasoBienvenida(PasoBienvenida.Mover);
        }
        return true;
    }

    /// <summary>La ruedita durante la bienvenida: solo cuenta (y cambia el volumen) en su paso.</summary>
    private bool RuedaEnBienvenida()
    {
        if (_modo != Modo.Bienvenida) return false;
        if (_pasoBienvenida != PasoBienvenida.Volumen) return true;   // en los otros pasos no hace nada
        CompletarPasoBienvenida(PasoBienvenida.Volumen);
        return false;   // que siga y cambie el volumen de verdad
    }

    /// <summary>El volumen cambio durante la bienvenida: se ve en la barra de la demo, sin salir del paso.</summary>
    private void MostrarVolumenEnBienvenida(float nivel, bool silenciado)
    {
        if (_pasoBienvenida != PasoBienvenida.Volumen) return;
        EscalaVolumenDemo.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(silenciado ? 0 : nivel, TimeSpan.FromMilliseconds(120)) { EasingFunction = new QuadraticEase() });
        PorcentajeVolumenDemo.Text = silenciado ? "" : ((int)Math.Round(nivel * 100)).ToString();
    }

    private void SiguienteBienvenida_Click(object sender, RoutedEventArgs e)
    {
        if (_pasoBienvenida == PasoBienvenida.Opciones) TerminarBienvenida(abrirOpciones: true);
        else AvanzarBienvenida(+1, desde: 40);
    }

    private void SaltarBienvenida_Click(object sender, RoutedEventArgs e) => TerminarBienvenida(abrirOpciones: false);

    private void TerminarBienvenida(bool abrirOpciones)
    {
        _avanceBienvenida.Stop();
        AnimarDemo(null);
        if (_pasoBienvenida == PasoBienvenida.Clima) GuardarCiudadBienvenida();
        if (_escribiendo) SoltarTeclado();
        if (!_config.BienvenidaVista)
        {
            _config.BienvenidaVista = true;
            _config.Guardar();
        }
        Log.Escribir(abrirOpciones ? "bienvenida: terminada" : $"bienvenida: salteada en {_pasoBienvenida}");

        if (!abrirOpciones)
        {
            Colapsar();
            return;
        }
        Expandir(Modo.Opciones, deslizarDesde: 40);
        _volverA[Modo.Opciones] = Modo.Herramientas;   // la flecha de Opciones no vuelve a la bienvenida
    }

    private void IdiomaBienvenida_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string codigo } || codigo == _config.Idioma) return;
        _config.Idioma = codigo;
        _config.Guardar();
        Idioma.Aplicar(codigo);
        foreach (RadioButton chip in ChipsIdioma.Children)
            chip.IsChecked = (string)chip.Tag == codigo;   // los de Opciones, al dia
    }

    private void CampoCiudadBienvenida_TextChanged(object sender, TextChangedEventArgs e) =>
        PistaCiudadBienvenida.Visibility = CampoCiudadBienvenida.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void CampoCiudad_TextChanged(object sender, TextChangedEventArgs e) =>
        PistaCiudad.Visibility = CampoCiudad.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void GuardarCiudadBienvenida()
    {
        if (_escribiendo) SoltarTeclado();
        string ciudad = CampoCiudadBienvenida.Text.Trim();
        if (Demo.Activo || ciudad == _config.Ciudad) return;   // en demo no se guarda nada
        _config.Ciudad = ciudad;
        _config.Guardar();
        CampoCiudad.Text = ciudad;
        if (_clima is not null) _clima.Ciudad = ciudad;
    }

    // ---------- Demos animadas (solo mientras se ve su paso: la ventana se redibuja por CPU) ----------

    private void AnimarDemo(PasoBienvenida? paso)
    {
        var carrusel = (TranslateTransform)CarruselDemo.RenderTransform;
        var dedo = (TranslateTransform)DedoDemo.RenderTransform;
        var pildora = (TranslateTransform)PildoraDemo.RenderTransform;
        var rueda = (TranslateTransform)RuedaDemo.RenderTransform;
        carrusel.BeginAnimation(TranslateTransform.XProperty, null);
        dedo.BeginAnimation(TranslateTransform.XProperty, null);
        DedoDemo.BeginAnimation(OpacityProperty, null);
        pildora.BeginAnimation(TranslateTransform.XProperty, null);
        pildora.BeginAnimation(TranslateTransform.YProperty, null);
        rueda.BeginAnimation(TranslateTransform.YProperty, null);

        switch (paso)
        {
            case PasoBienvenida.Paneles:
                // Tres paneles que pasan de a uno (y vuelven), con el "dedo" arrastrando en cada pase
                carrusel.BeginAnimation(TranslateTransform.XProperty, Recorrido(4.5,
                    (0, 0), (0.9, 0), (1.4, -150), (2.3, -150), (2.8, -300), (3.8, -300), (4.3, 0), (4.5, 0)));
                dedo.BeginAnimation(TranslateTransform.XProperty, Recorrido(4.5,
                    (0, 30), (0.9, 30), (1.4, -30), (1.8, 30), (2.3, 30), (2.8, -30), (4.5, -30)));
                DedoDemo.BeginAnimation(OpacityProperty, Recorrido(4.5,
                    (0, 0), (0.7, 0), (0.9, 1), (1.4, 1), (1.6, 0), (2.1, 0), (2.3, 1), (2.8, 1), (3.0, 0), (4.5, 0)));
                break;
            case PasoBienvenida.Mover:
                // La pildora en miniatura: arriba al centro → abajo a la izquierda → arriba a la derecha → vuelta
                pildora.BeginAnimation(TranslateTransform.XProperty, Recorrido(6,
                    (0, 0), (1, 0), (1.6, -26), (3, -26), (3.6, 26), (5, 26), (5.6, 0), (6, 0)));
                pildora.BeginAnimation(TranslateTransform.YProperty, Recorrido(6,
                    (0, -21), (1, -21), (1.6, 21), (3, 21), (3.6, -21), (6, -21)));
                break;
            case PasoBienvenida.Volumen:
                rueda.BeginAnimation(TranslateTransform.YProperty, Recorrido(1.2, (0, 0), (0.3, 4), (0.6, 0), (1.2, 0)));
                break;
        }
    }

    /// <summary>Animacion en bucle por puntos (segundo, valor), suavizada y a 30 cuadros.</summary>
    private static DoubleAnimationUsingKeyFrames Recorrido(double segundos, params (double En, double Valor)[] puntos)
    {
        var animacion = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(segundos),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        var curva = new CubicEase { EasingMode = EasingMode.EaseInOut };
        foreach (var (en, valor) in puntos)
            animacion.KeyFrames.Add(new EasingDoubleKeyFrame(valor, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(en)), curva));
        Timeline.SetDesiredFrameRate(animacion, 30);
        return animacion;
    }
}
