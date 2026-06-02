using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Diagnostics;
using System.Reflection;

namespace PenPressureProfiler;

/// <summary>
/// Modal "About" dialog: app name, version, and links to the GitHub repo
/// and README (opened in the default browser).
/// </summary>
public partial class AboutWindow : Window
{
    private const string RepoUrl   = "https://github.com/TheSevenPens/PenPressureProfiler";
    private const string ReadmeUrl = "https://github.com/TheSevenPens/PenPressureProfiler#readme";

    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        txt_version.Text = version is not null
            ? $"Version {version.Major}.{version.Minor}.{version.Build}"
            : "Version —";

        AddHandler(KeyDownEvent, OnKeyDown);
    }

    private void btn_repo_Click(object? sender, RoutedEventArgs e)   => OpenUrl(RepoUrl);
    private void btn_readme_Click(object? sender, RoutedEventArgs e) => OpenUrl(ReadmeUrl);
    private void Close_Click(object? sender, RoutedEventArgs e)      => Close();

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (System.Exception ex) { Debug.WriteLine($"[PPP2] Failed to open {url}: {ex.Message}"); }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }
}
