using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PenPressureProfiler.Views;

/// <summary>
/// Modal dialog for editing session metadata (brand, pen, tablet, driver,
/// inventory ID, etc). Returns the edited metadata via ShowDialog, or null
/// on Cancel.
/// </summary>
public partial class MetadataEditWindow : Window
{
    public MetadataEditWindow(PressureTestFile initial)
    {
        InitializeComponent();

        field_brand.Text       = initial.Brand;
        field_pen.Text         = initial.Pen;
        field_penfamily.Text   = initial.PenFamily;
        field_inventoryid.Text = initial.InventoryId;
        field_date.Text        = initial.Date;
        field_user.Text        = initial.User;
        field_tablet.Text      = initial.Tablet;
        field_driver.Text      = initial.Driver;
        field_os.Text          = initial.Os;
        field_tags.Text        = initial.Tags;
        textBox_notes.Text     = initial.Notes;

        AddHandler(KeyDownEvent, OnKeyDown);
    }

    private void Done_Click(object? sender, RoutedEventArgs e)
    {
        Close(new PressureTestFile
        {
            Brand       = field_brand.Text?.Trim()       ?? "",
            Pen         = field_pen.Text?.Trim()         ?? "",
            PenFamily   = field_penfamily.Text?.Trim()   ?? "",
            InventoryId = field_inventoryid.Text?.Trim() ?? "",
            Date        = field_date.Text?.Trim()        ?? "",
            User        = field_user.Text?.Trim()        ?? "",
            Tablet      = field_tablet.Text?.Trim()      ?? "",
            Driver      = field_driver.Text?.Trim()      ?? "",
            Os          = field_os.Text?.Trim()          ?? "",
            Tags        = field_tags.Text?.Trim()        ?? "",
            Notes       = textBox_notes.Text?.Trim()     ?? "",
        });
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }
}
