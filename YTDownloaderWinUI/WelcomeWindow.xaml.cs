using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        if (s.Hero || string.IsNullOrEmpty(s.ScreenshotFile))
        {
            HeroPanel.Visibility = Visibility.Visible;
            ShotFrame.Visibility = Visibility.Collapsed;
        }
        else
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "tutorial", s.ScreenshotFile);
            if (File.Exists(path))
            {
                ShotImage.Source = new BitmapImage(new Uri(path));
                ShotFrame.Visibility = Visibility.Visible;
                HeroPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                HeroPanel.Visibility = Visibility.Visible;
                ShotFrame.Visibility = Visibility.Collapsed;
            }
        }

        PrevBtn.IsEnabled = _index > 0;
        NextBtn.IsEnabled = _index < _steps.Count - 1;
        BuildDots();
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
