using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace YTDownloader;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        Title = "YT Downloader";
        AppWindow.Resize(new SizeInt32(960, 720));
    }

    public void SetTitleBarElement(UIElement element) => SetTitleBar(element);
}
