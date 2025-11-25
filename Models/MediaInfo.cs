namespace AirPulse.Models;

public class MediaInfo
{
    public string Title { get; set; } = "Unknown Title";
    public string Artist { get; set; } = "Unknown Artist";
    public string AlbumArtBase64 { get; set; } = "";
    public bool IsPlaying { get; set; } = false;
}
