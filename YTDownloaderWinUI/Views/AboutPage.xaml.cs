using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace YTDownloader.Views;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateBrandLogo();
        ActualThemeChanged += (_, _) => UpdateBrandLogo();
    }

    /// <summary>El logo de CG LABS se adapta al tema: negro en claro, blanco en oscuro.</summary>
    private void UpdateBrandLogo()
    {
        string file = ActualTheme == ElementTheme.Dark ? "cglabs_white.png" : "cglabs_black.png";
        CgLabsLogo.Source = new BitmapImage(new Uri($"ms-appx:///Assets/brand/{file}"));
    }

    private void BtnShowTutorial_Click(object sender, RoutedEventArgs e)
    {
        var w = new WelcomeWindow();
        w.Activate();
    }
}
