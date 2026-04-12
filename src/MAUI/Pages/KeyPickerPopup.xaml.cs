namespace DJBuddy.MAUI.Pages;

/// <summary>
/// Content view displayed inside an MCT popup for selecting a Camelot key filter.
/// Raises <see cref="KeySelected"/> when the user picks a key, and
/// <see cref="FilterCleared"/> when they press Clear.
/// Tapping outside the popup is handled by MCT and results in no event being raised.
/// </summary>
public partial class KeyPickerPopup : ContentView
{
    /// <summary>Raised with the chosen key string (e.g. "8A") when a key button is tapped.</summary>
    public event EventHandler<string>? KeySelected;

    /// <summary>Raised when the user presses the Clear button to remove the active filter.</summary>
    public event EventHandler? FilterCleared;

    /// <param name="currentFilter">The currently active key filter, or <c>null</c> if none.</param>
    public KeyPickerPopup(string? currentFilter)
    {
        InitializeComponent();
        PopulateKeyGrid(currentFilter);
    }

    private void PopulateKeyGrid(string? currentFilter)
    {
        for (int number = 1; number <= 12; number++)
        {
            KeyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            AddKeyButton($"{number}B", number - 1, 0, currentFilter);
            AddKeyButton($"{number}A", number - 1, 1, currentFilter);
        }
    }

    private void AddKeyButton(string key, int row, int col, string? currentFilter)
    {
        bool isSelected = string.Equals(key, currentFilter, StringComparison.OrdinalIgnoreCase);

        var button = new Button
        {
            Text = key,
            FontSize = 14,
            HeightRequest = 40,
            Padding = new Thickness(0),
            CornerRadius = 6,
            BackgroundColor = isSelected ? Colors.DodgerBlue : Colors.Transparent,
            TextColor = isSelected ? Colors.White : Colors.Gray,
            BorderColor = Colors.Gray,
            BorderWidth = 1,
        };

        button.Clicked += (_, _) => KeySelected?.Invoke(this, key);

        KeyGrid.SetRow(button, row);
        KeyGrid.SetColumn(button, col);
        KeyGrid.Children.Add(button);
    }

    /// <summary>Raises <see cref="FilterCleared"/> so the host can close the popup with an empty result.</summary>
    private void OnKeyPickerClear(object? sender, EventArgs e)
        => FilterCleared?.Invoke(this, EventArgs.Empty);
}
