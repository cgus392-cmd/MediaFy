using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace YTDownloader.Views;

public sealed partial class HomePage : Page
{
    public HomePage() => InitializeComponent();

    private void Go(string tag) => (App.MainWindow as MainWindow)?.NavigateTo(tag);

    private void Go_Downloads(object sender, RoutedEventArgs e) => Go("downloads");
    private void Go_Cascade(object sender, RoutedEventArgs e)   => Go("cascade");
    private void Go_Library(object sender, RoutedEventArgs e)   => Go("library");
    private void Go_Editor(object sender, RoutedEventArgs e)    => Go("editor");
}
