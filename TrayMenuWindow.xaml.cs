using System.Windows;

namespace AirPulse;

public partial class TrayMenuWindow : Window
{
    private readonly Action _onOpen;
    private readonly Action _onExit;

    public TrayMenuWindow(Action onOpen, Action onExit)
    {
        InitializeComponent();
        _onOpen = onOpen;
        _onExit = onExit;
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        _onOpen?.Invoke();
        Close();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _onExit?.Invoke();
        Close();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        Close();
    }
}
