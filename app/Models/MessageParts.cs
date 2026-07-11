using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace signal_windows_client.Models;

/// <summary>One attachment on a message: image/video render inline once
/// decoded; anything else shows as a generic file chip.</summary>
public sealed partial class AttachmentItem : ObservableObject
{
    public string? ContentType { get; init; }
    public string? FileName { get; init; }
    public uint? Size { get; init; }
    public byte[]? Data { get; init; }

    [ObservableProperty] private ImageSource? _image;
    /// <summary>Local temp-file path for video playback (written once from
    /// the decrypted bytes, since MediaPlayerElement wants a file/URI).</summary>
    [ObservableProperty] private string? _videoPath;

    public bool IsImage => ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
    public bool IsVideo => ContentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true;
    public bool IsFile => !IsImage && !IsVideo;

    public Visibility ImageVisibility => IsImage && Image != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VideoVisibility => IsVideo && VideoPath != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FileVisibility => IsFile || (Data == null && !IsImage && !IsVideo) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LoadingVisibility =>
        (IsImage && Image == null) || (IsVideo && VideoPath == null) ? Visibility.Visible : Visibility.Collapsed;

    public string DisplayName => string.IsNullOrEmpty(FileName) ? "Attachment" : FileName;
    public string SizeDisplay => Size is uint s ? $"{s / 1024.0:0.#} KB" : "";

    partial void OnImageChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(ImageVisibility));
        OnPropertyChanged(nameof(LoadingVisibility));
    }

    partial void OnVideoPathChanged(string? value)
    {
        OnPropertyChanged(nameof(VideoVisibility));
        OnPropertyChanged(nameof(LoadingVisibility));
    }
}

/// <summary>The message being replied to, shown as a snippet above the body.</summary>
public sealed class QuoteItem
{
    public long Timestamp { get; init; }
    public string Author { get; init; } = "";
    public string? Text { get; init; }
    public string AuthorDisplay { get; set; } = "";
    public bool HasText => !string.IsNullOrEmpty(Text);
}

/// <summary>A link preview card (title/description/image from the sender's own client).</summary>
public sealed partial class PreviewItem : ObservableObject
{
    public string Url { get; init; } = "";
    public string? Title { get; init; }
    public string? Description { get; init; }
    public byte[]? ImageBytes { get; init; }

    [ObservableProperty] private ImageSource? _image;

    public bool HasTitle => !string.IsNullOrEmpty(Title);
    public bool HasDescription => !string.IsNullOrEmpty(Description);
    public Visibility ImageVisibility => Image != null ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>One distinct emoji reaction on a message, with a count of who sent it.</summary>
public sealed partial class ReactionItem : ObservableObject
{
    public string Emoji { get; init; } = "";
    [ObservableProperty] private int _count;
    public string Display => Count > 1 ? $"{Emoji} {Count}" : Emoji;

    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(Display));
}
