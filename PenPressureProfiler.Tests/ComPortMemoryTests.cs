using PenPressureProfiler.Model;

namespace PenPressureProfiler.Tests;

public class ComPortChooserTests
{
    [Fact]
    public void Picks_remembered_port_when_present()
        => Assert.Equal("COM3", ComPortChooser.Choose(["COM1", "COM3", "COM7"], "COM3"));

    [Fact]
    public void Remembered_port_match_ignores_case()
        => Assert.Equal("COM3", ComPortChooser.Choose(["COM1", "COM3"], "com3"));

    [Fact]
    public void Falls_back_to_current_selection_when_remembered_port_is_gone()
        => Assert.Equal("COM1", ComPortChooser.Choose(["COM1", "COM7"], "COM3", current: "COM1"));

    [Fact]
    public void Falls_back_to_last_port_when_remembered_and_current_are_gone()
        => Assert.Equal("COM7", ComPortChooser.Choose(["COM1", "COM7"], "COM3", current: "COM9"));

    [Fact]
    public void Falls_back_to_last_port_when_nothing_remembered()
        => Assert.Equal("COM7", ComPortChooser.Choose(["COM1", "COM7"], null));

    [Fact]
    public void Returns_null_when_no_ports_exist()
        => Assert.Null(ComPortChooser.Choose([], "COM3", "COM3"));

    [Fact]
    public void Normalize_drops_blanks_and_duplicates()
        => Assert.Equal(["COM1", "COM3"], ComPortChooser.Normalize(["COM1", " ", "COM3", "com1"]));
}

public class UserSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ppp-settings-" + Guid.NewGuid().ToString("N"));
    private string PathInDir => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Round_trips_last_com_port()
    {
        new UserSettings { LastComPort = "COM5" }.Save(PathInDir);
        Assert.Equal("COM5", UserSettings.Load(PathInDir).LastComPort);
    }

    [Fact]
    public void Missing_file_gives_defaults()
        => Assert.Null(UserSettings.Load(PathInDir).LastComPort);

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathInDir, "{ not json");
        Assert.Null(UserSettings.Load(PathInDir).LastComPort);
    }
}
