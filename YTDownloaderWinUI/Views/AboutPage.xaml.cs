using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace YTDownloader.Views;

public sealed partial class AboutPage : Page
{
    public AboutPage() => InitializeComponent();

    private void BtnShowTutorial_Click(object sender, RoutedEventArgs e)
    {
        var w = new WelcomeWindow();
        w.Activate();
    }
}
