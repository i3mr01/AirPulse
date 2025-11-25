using AirPulse.Hubs;
using AirPulse.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;

namespace AirPulse;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        // Global exception handling
        DispatcherUnhandledException += (s, args) =>
        {
            System.Windows.MessageBox.Show($"An unhandled exception occurred: {args.Exception.Message}\n\nStack Trace:\n{args.Exception.StackTrace}", "AirPulse Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            // Create the host builder
        _host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseUrls("http://*:5000"); // Bind to all interfaces for LAN access
                webBuilder.ConfigureServices(services =>
                {
                    services.AddCors(options =>
                    {
                        options.AddPolicy("AllowAll", builder =>
                        {
                            builder.AllowAnyOrigin()
                                   .AllowAnyMethod()
                                   .AllowAnyHeader();
                        });
                    });
                    services.AddSignalR();
                    services.AddSingleton<InputService>();
                    services.AddSingleton<MediaWatcherService>();
                    // Register MainWindow to be resolved if needed, or just let WPF handle it
                });
                webBuilder.Configure(app =>
                {
                    app.UseDefaultFiles();
                    app.UseStaticFiles();
                    app.UseRouting();
                    app.UseCors("AllowAll");
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHub<MediaHub>("/mediaHub");
                    });
                    
                    // Start the MediaWatcherService
                    var watcher = app.ApplicationServices.GetService<MediaWatcherService>();
                });
            })
            .Build();

        await _host.StartAsync();

        var mainWindow = new MainWindow(_host.Services);
        mainWindow.Show();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Startup failed: {ex.Message}", "AirPulse Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            Shutdown();
        }
    }

    private async void Application_Exit(object sender, ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}
