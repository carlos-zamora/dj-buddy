using DJBuddy.MAUI.Pages;

namespace DJBuddy.MAUI;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("playlist", typeof(PlaylistPage));
        Routing.RegisterRoute("trackinfo", typeof(TrackInfoPage));
    }
}
