using AirPulse.Hubs;
using QRCoder;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using AirPulse.Services;

namespace AirPulse;

public partial class MainWindow : Window
{
    private readonly IServiceProvider _serviceProvider;
    private System.Windows.Forms.NotifyIcon _notifyIcon;
    private bool _isExiting = false;

    public MainWindow(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
        GeneratePin();
        DisplayConnectionInfo();
        InitializeTrayIcon();
        CheckAutoStart();
        EnsureFirewallRule();

        var watcher = _serviceProvider.GetService<MediaWatcherService>();
        if (watcher != null)
        {
            watcher.ClientConnected += OnClientConnected;
            watcher.ClientDisconnected += OnClientDisconnected;
            // Initialize on UI thread
            watcher.StartAsync();
        }
    }

    private void OnClientConnected(string connectionId)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = "Connected to Phone";
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LightGreen);
        });
    }

    private void OnClientDisconnected(string connectionId)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = "Waiting for connections...";
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 102, 102)); // #666666
            
            // Regenerate credentials for security
            GeneratePin();
            UpdateIpDisplay();
        });
    }

    private void EnsureFirewallRule()
    {
        try
        {
            // Check if we can run the command (this is a basic "fire and forget" attempt)
            // We use a separate process to request admin rights if needed
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "advfirewall firewall add rule name=\"AirPulse\" dir=in action=allow protocol=TCP localport=5000",
                Verb = "runas", // Trigger UAC
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception)
        {
            // User might have cancelled UAC or we don't have permission. 
            // We can't do much else without annoying the user.
        }
    }

    private void InitializeTrayIcon()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location),
            Visible = true,
            Text = "AirPulse Server"
        };
        
        _notifyIcon.MouseUp += NotifyIcon_MouseUp;
    }

    private void NotifyIcon_MouseUp(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (e.Button == System.Windows.Forms.MouseButtons.Left || e.Button == System.Windows.Forms.MouseButtons.Right)
        {
            ShowTrayMenu();
        }
    }

    private void ShowTrayMenu()
    {
        var menu = new TrayMenuWindow(() => ShowWindow(), () => ExitApplication());
        
        // Get mouse position in screen coordinates
        var mouse = System.Windows.Forms.Cursor.Position;
        
        // Convert to DPI-aware coordinates
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            var transform = source.CompositionTarget.TransformFromDevice;
            var dpiMouse = transform.Transform(new System.Windows.Point(mouse.X, mouse.Y));
            
            menu.Left = dpiMouse.X - menu.Width;
            menu.Top = dpiMouse.Y - menu.Height;
        }
        else
        {
            // Fallback if source is not available (e.g. window hidden)
            // Just use raw pixels, might be off but better than nothing
            menu.Left = mouse.X - menu.Width;
            menu.Top = mouse.Y - menu.Height;
        }

        // Ensure it's on screen
        if (menu.Top < 0) menu.Top = 0;
        if (menu.Left < 0) menu.Left = 0;

        menu.Show();
        menu.Activate();
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
        base.OnStateChanged(e);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExiting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    private void CheckAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
        var value = key?.GetValue("AirPulse");
        if (value != null)
        {
            AutoStartToggle.IsChecked = true;
        }
    }

    private void AutoStartCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        SetAutoStart(true);
    }

    private void AutoStartCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        SetAutoStart(false);
    }

    private void SetAutoStart(bool enable)
    {
        const string keyName = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        using var key = Registry.CurrentUser.OpenSubKey(keyName, true);
        if (enable)
        {
            key?.SetValue("AirPulse", Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe"));
        }
        else
        {
            key?.DeleteValue("AirPulse", false);
        }
    }

    private void GeneratePin()
    {
        var random = new Random();
        var pin = random.Next(0, 999999).ToString("D6");
        MediaHub.CurrentPin = pin;
        PinText.Text = $"{pin.Substring(0, 3)} {pin.Substring(3, 3)}";
    }

    private List<string> _availableIps = new();
    private int _currentIpIndex = 0;

    private void DisplayConnectionInfo()
    {
        _availableIps = GetLocalIpAddresses();
        if (_availableIps.Count > 0)
        {
            UpdateIpDisplay();
        }
        else
        {
            IpText.Text = "No Network Found";
        }
    }

    private void UpdateIpDisplay()
    {
        if (_availableIps.Count == 0) return;
        string localIp = _availableIps[_currentIpIndex];
        string url = $"http://{localIp}:5000";
        IpText.Text = url;
        GenerateQrCode(url);
    }

    private void IpText_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_availableIps.Count > 1)
        {
            _currentIpIndex = (_currentIpIndex + 1) % _availableIps.Count;
            UpdateIpDisplay();
        }
    }

    private void FixConnection_Click(object sender, RoutedEventArgs e)
    {
        EnsureFirewallRule();
        System.Windows.MessageBox.Show("Firewall rule update attempted.\n\nIf it still doesn't work:\n1. Check that your phone is on the SAME Wi-Fi.\n2. Try clicking the IP address to switch network adapters.", "Connection Fix", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private List<string> GetLocalIpAddresses()
    {
        var ips = new List<string>();

        // Try to find the best match (Wi-Fi or Ethernet)
        foreach (var netInterface in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            // Only consider interfaces that are actually UP and not Loopback
            if (netInterface.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up ||
                netInterface.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
            {
                continue;
            }

            if (netInterface.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 ||
                netInterface.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet)
            {
                foreach (var ip in netInterface.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string address = ip.Address.ToString();
                        // Filter out APIPA (169.254.x.x) which means no DHCP
                        if (!address.StartsWith("169.254"))
                        {
                            ips.Add(address);
                        }
                    }
                }
            }
        }

        // Fallback: Return the first non-169.254 IPv4 address found
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                string address = ip.ToString();
                if (!address.StartsWith("169.254") && !ips.Contains(address))
                {
                    ips.Add(address);
                }
            }
        }
        
        if (ips.Count == 0) ips.Add("127.0.0.1");
        return ips;
    }

    private void GenerateQrCode(string url)
    {
        // Embed the PIN in the URL fragment or query param so the client can auto-fill if we wanted
        // For now, just the URL as requested, but let's be smart and add the PIN for convenience if the client supports it
        // The user requirement said "QR contains URL... The pairing PIN encoded inside"
        // So let's do: http://IP:5000/?pin=123456
        
        // So let's do: http://IP:5000/?pin=123456
        
        string payload = $"{url}/?pin={MediaHub.CurrentPin}";
        if (url.EndsWith("/"))
        {
             payload = $"{url}?pin={MediaHub.CurrentPin}";
        }

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new QRCode(qrCodeData);
        using var bitmap = qrCode.GetGraphic(20);
        
        QrCodeImage.Source = BitmapToImageSource(bitmap);
    }

    private BitmapImage BitmapToImageSource(Bitmap bitmap)
    {
        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        memory.Position = 0;
        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.StreamSource = memory;
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.EndInit();
        return bitmapImage;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            this.DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}
