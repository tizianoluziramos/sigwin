using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using signal_windows_client.ViewModels;

namespace signal_windows_client.Views;

public sealed partial class LinkingPage : Page
{
    public LinkingViewModel Vm => App.LinkingVm;

    public LinkingPage()
    {
        InitializeComponent();
    }

    // x:Bind function helpers.
    public Visibility Vis(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
    public bool Not(bool b) => !b;
}
