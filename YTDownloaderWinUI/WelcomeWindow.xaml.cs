using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using YTDownloader.Core;
using Path = System.IO.Path;
using File = System.IO.File;

namespace YTDownloader;

public sealed partial class WelcomeWindow : Window
{
    private record Step(string Title, string Description, string? ScreenshotFile, bool Hero = false);

    private readonly List<Step> _steps;
    private int _index;
    private string _firstName = "tú";

    public WelcomeWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        Title = "Introducción a MediaFy";
        AppWindow.Resize(new SizeInt32(1080, 720));
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico")); } catch { }
        // Centrar la ventana
        try
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            AppWindow.Move(new PointInt32(
                area.X + (area.Width  - 1080) / 2,
                area.Y + (area.Height - 720) / 2));
        }
        catch { }

        _steps = BuildSteps();
        Activated += OnFirstActivated;
    }

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;

        // Usuario de Windows (nombre + foto)
        _firstName = await WindowsUser.GetFirstNameAsync();
        AvatarInitial.Text = string.IsNullOrEmpty(_firstName) ? "?" : _firstName[0].ToString().ToUpper();

        string? pic = await WindowsUser.GetPictureAsync();
        if (pic != null && File.Exists(pic))
        {
            AvatarImage.ImageSource = new BitmapImage(new Uri(pic));
            AvatarImageEllipse.Visibility = Visibility.Visible;
        }

        BuildDots();
        Render();
    }

    private List<Step> BuildSteps() => new()
    {
        new Step(
            $"Te damos la bienvenida a MediaFy",  // se personaliza en Render
            "MediaFy es tu gestor de descargas multiplataforma. Vamos a hacer un recorrido rápido por lo que puede hacer.",
            null, Hero: true),
        new Step(
            "Descarga de casi cualquier sitio",
            "Pega un enlace de YouTube, Spotify, SoundCloud, TikTok, Vimeo y cientos de sitios más. MediaFy detecta la plataforma y te muestra una vista previa antes de descargar.",
            "downloads.png"),
        new Step(
            "Descargas en cascada",
            "Agrega varios enlaces y MediaFy los descargará en escalonado: la siguiente arranca cuando la anterior llega al 70%. Suave, sin saturar la red.",
            "cascade.png"),
        new Step(
            "Biblioteca con reproductor integrado",
            "Tus archivos aparecen aquí con sus portadas. Reprodúcelos con el mini-reproductor flotante o el reproductor global, que te acompaña por toda la app.",
            "library.png"),
        new Step(
            "Editor con forma de onda",
            "Recorta fragmentos de tus archivos con precisión: arrastra los marcadores sobre la onda, escucha la selección, aplica fades y guarda.",
            "editor.png"),
        new Step(
            "Y mucho más bajo el capó",
            "Monitor de recursos en vivo, telones de fondo Mica/Acrílico configurables, organizador dual-pane, extensión de navegador, vigilancia del portapapeles, autoactualización… está todo dentro.",
            "monitor.png"),
        new Step(
            "¡Listo para empezar!",
            "Explora con calma. Si quieres volver a ver esta introducción, está en Acerca de → Ver introducción.",
            null, Hero: true)
    };

    private void BuildDots()
    {
        StepDots.Items.Clear();
        for (int i = 0; i < _steps.Count; i++)
        {
            StepDots.Items.Add(new Ellipse
            {
                Width = 7, Height = 7,
                Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                    i == _index ? "AccentFillColorDefaultBrush" : "ControlStrokeColorDefaultBrush"]
            });
        }
    }

    private void Render()
    {
        var s = _steps[_index];

        // Personalizar el primer paso con el nombre
        string title = _index == 0 ? $"Te damos la bienvenida, {_firstName}" : s.Title;
        TitleText.Text = title;
        DescText.Text = s.Description;

        bool isLast = _index == _steps.Count - 1;
        bool isFirst = _index == 0;
        PrimaryButtonText.Text = isLast ? "Empezar a usar MediaFy" :
                                 isFirst ? "Comenzar" : "Siguiente";

        // Mockup
        bool showHero = s.Hero || string.IsNullOrEmpty(s.ScreenshotFile);
        string path = showHero ? "" : Path.Combine(AppContext.BaseDirectory, "Assets", "tutorial", s.ScreenshotFile!);
        if (!showHero && !File.Exists(path)) showHero = true;

        if (showHero)
        {
            HeroPanel.Visibility = Visibility.Visible;
            ShotFrame.Visibility = Visibility.Collapsed;
        }
        else
        {
            ShotImage.Source = new BitmapImage(new Uri(path));
            ShotFrame.Visibility = Visibility.Visible;
            HeroPanel.Visibility = Visibility.Collapsed;
        }

        PrevBtn.IsEnabled = _index > 0;
        NextBtn.IsEnabled = _index < _steps.Count - 1;
        BuildDots();

        // Animaciones de entrada (que se sienta vivo)
        AnimateTextIn();
        if (showHero) PlayHeroIntro();
        else AnimateMockupIn();
    }

    // ---------------------------------------------------------------
    //  Animaciones (Composition) — entrada espectacular del tutorial
    // ---------------------------------------------------------------
    private bool _heroBreathing;

    private void AnimateTextIn()
    {
        SlideFadeUp(TitleText, 30f, 0, 620);
        SlideFadeUp(DescText, 30f, 90, 620);
        SlideFadeUp(PrimaryButton, 30f, 180, 620);
    }

    private void AnimateMockupIn()
    {
        var v = ElementCompositionPreview.GetElementVisual(ShotFrame);
        var comp = v.Compositor;
        v.CenterPoint = new Vector3(250f, 250f, 0f); // 500/2
        v.Opacity = 0f;
        v.Scale = new Vector3(0.94f);

        var fade = comp.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 1f);
        fade.Duration = TimeSpan.FromMilliseconds(500);
        v.StartAnimation("Opacity", fade);

        var ease = comp.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));
        var scale = comp.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(1f, Vector3.One, ease);
        scale.Duration = TimeSpan.FromMilliseconds(640);
        v.StartAnimation("Scale", scale);
    }

    private void PlayHeroIntro()
    {
        // Logo: aparece con un resorte elastico
        var logo = ElementCompositionPreview.GetElementVisual(HeroLogo);
        var comp = logo.Compositor;
        logo.CenterPoint = new Vector3(80f, 80f, 0f);
        logo.Opacity = 0f;
        logo.Scale = new Vector3(0.4f);

        var logoFade = comp.CreateScalarKeyFrameAnimation();
        logoFade.InsertKeyFrame(1f, 1f);
        logoFade.Duration = TimeSpan.FromMilliseconds(450);
        logo.StartAnimation("Opacity", logoFade);

        var spring = comp.CreateSpringVector3Animation();
        spring.FinalValue = Vector3.One;
        spring.DampingRatio = 0.42f;
        spring.Period = TimeSpan.FromMilliseconds(55);
        logo.StartAnimation("Scale", spring);

        // Halos: se desvanecen hacia dentro, escalonados
        HaloFadeIn(HeroHalo3, 0.05f, 120);
        HaloFadeIn(HeroHalo1, 0.08f, 60);
        HaloFadeIn(HeroHalo2, 0.12f, 0);

        // ...y luego respiran en bucle infinito (una sola vez)
        if (!_heroBreathing)
        {
            _heroBreathing = true;
            Breathe(HeroHalo3, 170f, 1.0f, 1.06f, 5200, 0);
            Breathe(HeroHalo1, 140f, 1.0f, 1.09f, 4400, 350);
            Breathe(HeroHalo2, 100f, 1.0f, 1.13f, 3600, 700);
        }
    }

    private void HaloFadeIn(UIElement el, float baseOpacity, double delayMs)
    {
        var v = ElementCompositionPreview.GetElementVisual(el);
        var comp = v.Compositor;
        v.Opacity = 0f;
        var fade = comp.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, baseOpacity);
        fade.Duration = TimeSpan.FromMilliseconds(650);
        fade.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        v.StartAnimation("Opacity", fade);
    }

    private void Breathe(UIElement el, float half, float min, float max, double periodMs, double delayMs)
    {
        var v = ElementCompositionPreview.GetElementVisual(el);
        var comp = v.Compositor;
        v.CenterPoint = new Vector3(half, half, 0f);

        var io = comp.CreateCubicBezierEasingFunction(new Vector2(0.45f, 0f), new Vector2(0.55f, 1f));
        var anim = comp.CreateVector3KeyFrameAnimation();
        anim.InsertKeyFrame(0f, new Vector3(min));
        anim.InsertKeyFrame(0.5f, new Vector3(max), io);
        anim.InsertKeyFrame(1f, new Vector3(min), io);
        anim.Duration = TimeSpan.FromMilliseconds(periodMs);
        anim.IterationBehavior = AnimationIterationBehavior.Forever;
        if (delayMs > 0)
        {
            anim.DelayTime = TimeSpan.FromMilliseconds(delayMs);
            anim.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        }
        v.StartAnimation("Scale", anim);
    }

    private void SlideFadeUp(UIElement el, float fromY, double delayMs, double durMs)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(el, true);
        var v = ElementCompositionPreview.GetElementVisual(el);
        var comp = v.Compositor;
        v.Opacity = 0f;
        v.Properties.InsertVector3("Translation", new Vector3(0f, fromY, 0f));

        var fade = comp.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 1f);
        fade.Duration = TimeSpan.FromMilliseconds(durMs);
        fade.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        v.StartAnimation("Opacity", fade);

        var ease = comp.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));
        var move = comp.CreateVector3KeyFrameAnimation();
        move.InsertKeyFrame(1f, Vector3.Zero, ease);
        move.Duration = TimeSpan.FromMilliseconds(durMs);
        move.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        move.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        v.StartAnimation("Translation", move);
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (_index >= _steps.Count - 1) { Finish(); return; }
        _index++;
        Render();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_index < _steps.Count - 1) { _index++; Render(); }
    }
    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_index > 0) { _index--; Render(); }
    }
    private void Home_Click(object sender, RoutedEventArgs e) { _index = 0; Render(); }

    private void Finish()
    {
        AppSettings.Current.WelcomeShown = true;
        Close();
    }
}
