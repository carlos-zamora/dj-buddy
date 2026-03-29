using dj_buddy.Pages;

namespace dj_buddy;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("playlist", typeof(PlaylistPage));
    }
}
