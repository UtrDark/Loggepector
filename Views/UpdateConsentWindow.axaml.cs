using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Loggepector.Views;

public partial class UpdateConsentWindow : Window
{
    public UpdateConsentWindow() => InitializeComponent();
    private void Approve(object? sender, RoutedEventArgs e) => Close(true);
    private void Decline(object? sender, RoutedEventArgs e) => Close(false);
}
