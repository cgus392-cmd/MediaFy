using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;
using YTDownloader.Views;

namespace YTDownloader;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Title = "YT Downloader";
        AppWindow.Resize(new SizeInt32(1000, 740));

        ContentFrame.Navigate(typeof(DownloadsPage));
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItemContainer is NavigationViewItem item)
        {
            switch (item.Tag?.ToString())
            {
                case "downloads": ContentFrame.Navigate(typeof(DownloadsPage)); break;
                case "library":   ContentFrame.Navigate(typeof(LibraryPage));   break;
            }
        }
    }
}
