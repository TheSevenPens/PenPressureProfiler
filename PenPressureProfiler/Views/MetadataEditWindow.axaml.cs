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
    // Fields no longer surfaced in the dialog (Pen family, User, Tags, Notes)
    // are carried through unchanged so editing metadata never wipes values
    // present in a loaded file.
    private readonly SessionMetadata _initial;

    // When true, Done is blocked until every visible field is filled in.
    private readonly bool _requireAll;

    public MetadataEditWindow(SessionMetadata initial, bool requireAll = false)
    {
        InitializeComponent();

        _initial    = initial;
        _requireAll = requireAll;

        field_brand.Text       = initial.Brand;
        field_pen.Text         = initial.Pen;
        field_inventoryid.Text = initial.InventoryId;
        field_date.Text        = initial.Date;
        field_tablet.Text      = initial.Tablet;
        field_driver.Text      = initial.Driver;
        field_os.Text          = initial.Os;

        AddHandler(KeyDownEvent, OnKeyDown);
    }

    private void Done_Click(object? sender, RoutedEventArgs e)
    {
        if (_requireAll)
        {
            var missing = new List<string>();
            void Check(string label, string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) missing.Add(label);
            }
            Check("Brand",        field_brand.Text);
            Check("Pen",          field_pen.Text);
            Check("Inventory ID", field_inventoryid.Text);
            Check("Date",         field_date.Text);
            Check("Tablet",       field_tablet.Text);
            Check("Driver",       field_driver.Text);
            Check("OS",           field_os.Text);

            if (missing.Count > 0)
            {
                error_text.Text      = "Required: " + string.Join(", ", missing);
                error_text.IsVisible = true;
                return;
            }
        }

        Close(new SessionMetadata
        {
            Brand       = field_brand.Text?.Trim()       ?? "",
            Pen         = field_pen.Text?.Trim()         ?? "",
            PenFamily   = _initial.PenFamily,
            InventoryId = field_inventoryid.Text?.Trim() ?? "",
            Date        = field_date.Text?.Trim()        ?? "",
            User        = _initial.User,
            Tablet      = field_tablet.Text?.Trim()      ?? "",
            Driver      = field_driver.Text?.Trim()      ?? "",
            Os          = field_os.Text?.Trim()          ?? "",
            Tags        = _initial.Tags,
            Notes       = _initial.Notes,
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
