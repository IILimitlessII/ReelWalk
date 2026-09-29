using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ReelWalk.Views;
internal static class Notice
{
    // A small window with one message and an OK button.
    // title is the caption. message is the body. Returns the window.
    internal static Window Create(string title, string message)
    {
        var window = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.Parse("#161616")),
            CanResize = false
        };
        var ok = new Button
        {
            Content = "OK",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 16, 0, 0),
            Padding = new Avalonia.Thickness(18, 8)
        };
        ok.Click += (s, e) => window.Close();
        window.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14
                },
                ok
            }
        };
        return window;
    }

    // Shows message on owner and waits until it closes.
    // Returns nothing.
    internal static Task Show(Window owner, string title, string message)
    {
        var window = Create(title, message);
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        return window.ShowDialog(owner);
    }

    // Asks a yes or no question.
    // Returns true when Yes is chosen.
    internal static async Task<bool> Confirm(Window owner, string title, string message)
    {
        bool yes = false;
        var window = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#161616")),
            CanResize = false
        };
        var yesButton = new Button
        {
            Content = "Yes",
            Margin = new Avalonia.Thickness(0, 16, 8, 0),
            Padding = new Avalonia.Thickness(18, 8)
        };
        var noButton = new Button
        {
            Content = "No",
            Margin = new Avalonia.Thickness(0, 16, 0, 0),
            Padding = new Avalonia.Thickness(18, 8)
        };
        yesButton.Click += (s, e) =>
        {
            yes = true;
            window.Close();
        };
        noButton.Click += (s, e) => window.Close();
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { yesButton, noButton }
        };
        window.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14
                },
                row
            }
        };
        await window.ShowDialog(owner);
        return yes;
    }
}
