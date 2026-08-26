using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace YTDownloader.Views;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();

        // Versión y año se leen del ensamblado y del reloj: estaban escritos a mano y quedaban
        // desfasados en cada release (la app mostraba 1.7.0 siendo ya la 2.0.1).
        TxtVersion.Text = $"Versión {Core.UpdateService.CurrentVersion()}";
        TxtCopyright.Text = $"© {DateTime.Now.Year} CG LABS";

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

    private async void BtnTerms_Click(object sender, RoutedEventArgs e)
        => await TermsDialog.ShowAsync(XamlRoot, requireAccept: false);
}
