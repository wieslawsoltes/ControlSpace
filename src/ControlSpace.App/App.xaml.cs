using ControlSpace.Workbench.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace ControlSpace.App;

public sealed partial class App : Application
{
    private Window? _window;
    private WorkbenchView? _workbench;
    public App() { InitializeComponent(); UnhandledException += (_, e) => Console.Error.WriteLine("[ControlSpace] " + e.Exception); }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
#if __WASM__
        Uno.WinRTFeatureConfiguration.Storage.Pickers.WasmConfiguration = Uno.WasmPickerConfiguration.DownloadUpload;
#endif
        _window = new Window { Title = "ControlSpace" }; _window.Content = new TextBlock { Text = "Starting ControlSpace…", Margin = new Thickness(30), FontSize = 22 }; _window.Activate();
        try
        {
            _workbench = new WorkbenchView(new PlatformProjectFiles()); _window.Content = _workbench; await _workbench.InitializeAsync();
            _window.Closed += (_, _) => _workbench.Dispose(); Console.WriteLine("[ControlSpace] Uno workspace ready");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex); _window.Content = new ScrollViewer { Content = new TextBlock { Text = "ControlSpace could not start.\n\n" + ex, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Margin = new Thickness(24) } };
        }
    }
}
