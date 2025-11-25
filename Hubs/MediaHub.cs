using AirPulse.Models;
using AirPulse.Services;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace AirPulse.Hubs;

public class MediaHub : Hub
{
    private readonly InputService _inputService;
    private readonly MediaWatcherService _mediaWatcher;
    
    // Static to persist across transient hub instances
    public static string CurrentPin { get; set; } = "000000";
    private static ConcurrentDictionary<string, bool> _authenticatedConnections = new();

    public MediaHub(InputService inputService, MediaWatcherService mediaWatcher)
    {
        _inputService = inputService;
        _mediaWatcher = mediaWatcher;
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        // Send current state immediately upon connection (even if not authenticated yet, 
        // though UI might hide it. Alternatively, send only after auth).
        // For security, we might want to wait, but for responsiveness, sending play state is low risk.
        // We will enforce auth for ACTIONS.
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (_authenticatedConnections.TryRemove(Context.ConnectionId, out _))
        {
            _mediaWatcher.NotifyClientDisconnected(Context.ConnectionId);
        }
        return base.OnDisconnectedAsync(exception);
    }

    public async Task<bool> Pair(string pin)
    {
        if (pin == CurrentPin)
        {
            _authenticatedConnections.TryAdd(Context.ConnectionId, true);
            
            // Force refresh media info
            await _mediaWatcher.UpdateMediaInfoAsync();
            
            // Send full media info upon successful pairing
            await Clients.Caller.SendAsync("ReceiveMediaUpdate", _mediaWatcher.CurrentMediaInfo);
            _mediaWatcher.NotifyClientConnected(Context.ConnectionId);
            return true;
        }
        return false;
    }

    public void SendInput(string command)
    {
        if (!IsAuthenticated()) return;
        _inputService.SendMediaKey(command);
    }

    public void SendMouse(int deltaX, int deltaY)
    {
        if (!IsAuthenticated()) return;
        _inputService.MoveMouse(deltaX, deltaY);
    }

    public void SendClick()
    {
        if (!IsAuthenticated()) return;
        _inputService.LeftClick();
    }

    private bool IsAuthenticated()
    {
        return _authenticatedConnections.ContainsKey(Context.ConnectionId);
    }
}
