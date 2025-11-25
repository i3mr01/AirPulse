using AirPulse.Hubs;
using AirPulse.Models;
using Microsoft.AspNetCore.SignalR;
using Windows.Media.Control;
using Windows.Storage.Streams;
using System.IO;

namespace AirPulse.Services;

public class MediaWatcherService
{
    private readonly IHubContext<MediaHub> _hubContext;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    
    public MediaInfo CurrentMediaInfo { get; private set; } = new();

    public event Action<string>? ClientConnected;
    public event Action<string>? ClientDisconnected;

    public void NotifyClientConnected(string connectionId)
    {
        ClientConnected?.Invoke(connectionId);
    }

    public void NotifyClientDisconnected(string connectionId)
    {
        ClientDisconnected?.Invoke(connectionId);
    }

    public MediaWatcherService(IHubContext<MediaHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task StartAsync()
    {
        await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try 
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_manager != null)
            {
                _manager.CurrentSessionChanged += Manager_CurrentSessionChanged;
                UpdateSession(_manager.GetCurrentSession());
                Console.WriteLine("MediaWatcher: Initialized successfully.");
            }
            else
            {
                Console.WriteLine("MediaWatcher: Manager request returned null.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"MediaWatcher: Initialization failed: {ex.Message}");
        }
    }

    private void Manager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        Console.WriteLine("MediaWatcher: Session changed.");
        UpdateSession(sender.GetCurrentSession());
    }

    private void UpdateSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
        }

        _currentSession = session;

        if (_currentSession != null)
        {
            Console.WriteLine("MediaWatcher: New session attached.");
            _currentSession.MediaPropertiesChanged += Session_MediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged += Session_PlaybackInfoChanged;
            UpdateMediaInfoAsync();
        }
        else
        {
            Console.WriteLine("MediaWatcher: Session lost/null.");
            CurrentMediaInfo.IsPlaying = false;
            BroadcastUpdate();
        }
    }

    private void Session_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        UpdateMediaInfoAsync();
    }

    private void Session_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        UpdateMediaInfoAsync();
    }

    public async Task UpdateMediaInfoAsync()
    {
        // Robustness: Try to initialize if missing
        if (_manager == null)
        {
            Console.WriteLine("MediaWatcher: Manager null, retrying request...");
            try 
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_manager != null)
                {
                    _manager.CurrentSessionChanged += Manager_CurrentSessionChanged;
                    UpdateSession(_manager.GetCurrentSession());
                }
            }
            catch (Exception ex) { Console.WriteLine($"MediaWatcher: Retry failed: {ex.Message}"); }
        }

        // Robustness: Try to refresh session if valid manager but no session
        if (_currentSession == null && _manager != null)
        {
             Console.WriteLine("MediaWatcher: Session null, refreshing from manager...");
             UpdateSession(_manager.GetCurrentSession());
        }

        if (_currentSession == null) 
        {
            Console.WriteLine("MediaWatcher: No active session found to update.");
            return;
        }

        try
        {
            var props = await _currentSession.TryGetMediaPropertiesAsync();
            var playback = _currentSession.GetPlaybackInfo();

            if (props != null)
            {
                CurrentMediaInfo.Title = props.Title;
                CurrentMediaInfo.Artist = props.Artist;
                CurrentMediaInfo.IsPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                if (props.Thumbnail != null)
                {
                    using var stream = await props.Thumbnail.OpenReadAsync();
                    using var memoryStream = new MemoryStream();
                    await stream.AsStreamForRead().CopyToAsync(memoryStream);
                    CurrentMediaInfo.AlbumArtBase64 = Convert.ToBase64String(memoryStream.ToArray());
                }
                else 
                {
                    CurrentMediaInfo.AlbumArtBase64 = "";
                }
                
                Console.WriteLine($"MediaWatcher: Updated info - {CurrentMediaInfo.Title} ({CurrentMediaInfo.IsPlaying})");
                BroadcastUpdate();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"MediaWatcher: Update error: {ex.Message}");
        }
    }

    private async void BroadcastUpdate()
    {
        await _hubContext.Clients.All.SendAsync("ReceiveMediaUpdate", CurrentMediaInfo);
    }
}
