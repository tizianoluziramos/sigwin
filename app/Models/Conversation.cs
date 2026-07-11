using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace signal_windows_client.Models;

/// <summary>A conversation (1:1 or group) and its loaded messages.</summary>
public sealed partial class Conversation : ObservableObject
{
    public string Id { get; }
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    [ObservableProperty] private string _title;
    [ObservableProperty] private string _lastPreview = "";
    [ObservableProperty] private DateTimeOffset _lastTime;
    [ObservableProperty] private ImageSource? _avatar;
    [ObservableProperty] private bool _isGroup;
    [ObservableProperty] private bool _isTyping;

    public bool HasAvatar => Avatar != null;
    public Visibility AvatarVisibility => HasAvatar ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InitialsVisibility => HasAvatar ? Visibility.Collapsed : Visibility.Visible;
    // Diagnostic: a small badge on the avatar so it's visually obvious
    // whether an entry is a group or a 1:1 contact.
    public Visibility GroupBadgeVisibility => IsGroup ? Visibility.Visible : Visibility.Collapsed;
    public string HeaderSubtitle => IsTyping ? "typing…" : "";
    public Visibility TypingVisibility => IsTyping ? Visibility.Visible : Visibility.Collapsed;

    partial void OnIsGroupChanged(bool value) => OnPropertyChanged(nameof(GroupBadgeVisibility));
    partial void OnIsTypingChanged(bool value)
    {
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(TypingVisibility));
    }

    partial void OnAvatarChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(HasAvatar));
        OnPropertyChanged(nameof(AvatarVisibility));
        OnPropertyChanged(nameof(InitialsVisibility));
    }

    public Conversation(string id, string title)
    {
        Id = id;
        _title = title;
    }

    public string Initials => ComputeInitials(Title);
    public string LastTimeDisplay => Humanize(LastTime);
    public Brush AvatarBrush => BrushFor(Id);

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(Initials));
    partial void OnLastTimeChanged(DateTimeOffset value) => OnPropertyChanged(nameof(LastTimeDisplay));

    private static string ComputeInitials(string s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return "?";
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[1][0])}"
            : char.ToUpper(s[0]).ToString();
    }

    private static string Humanize(DateTimeOffset t)
    {
        if (t == default) return "";
        var local = t.ToLocalTime();
        var now = DateTimeOffset.Now;
        if (local.Date == now.Date) return local.ToString("HH:mm");
        if (local.Date == now.Date.AddDays(-1)) return "Yesterday";
        if ((now - local).TotalDays < 7) return local.ToString("ddd");
        return local.ToString("MMM d");
    }

    private static readonly Color[] Palette =
    {
        Color.FromArgb(255, 0x6A, 0x5A, 0xCD), Color.FromArgb(255, 0x2E, 0x8B, 0x57),
        Color.FromArgb(255, 0xC7, 0x6E, 0x00), Color.FromArgb(255, 0xB0, 0x30, 0x60),
        Color.FromArgb(255, 0x2B, 0x78, 0xC2), Color.FromArgb(255, 0x8A, 0x2B, 0xE2),
    };

    private static Brush BrushFor(string id)
    {
        int h = 0;
        foreach (var c in id) h = (h * 31 + c) & 0x7fffffff;
        return new SolidColorBrush(Palette[h % Palette.Length]);
    }
}
