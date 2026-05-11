using System.Windows.Media.Imaging;

namespace WinNotch.Models;

/// <summary>
/// Immutable snapshot of the currently playing media track.
/// Passed from the MediaService to the ViewModel.
/// </summary>
public record MediaInfo(
    string Title,
    string Artist,
    string Album,
    BitmapSource? AlbumArt,
    bool IsPlaying
)
{
    /// <summary>Represents "nothing is playing" state.</summary>
    public static readonly MediaInfo Empty = new(
        Title: string.Empty,
        Artist: string.Empty,
        Album: string.Empty,
        AlbumArt: null,
        IsPlaying: false
    );
}
