using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace signal_windows_client.Models;

/// <summary>A single message in a conversation thread.</summary>
public sealed partial class ChatMessage : ObservableObject
{
    /// <summary>Stable identity for edits/deletes/reactions: a message is
    /// uniquely identified by (thread, sender, original send timestamp).</summary>
    public string ThreadId { get; init; } = "";
    public string SenderUuid { get; init; } = "";
    public long Timestamp { get; init; }

    [ObservableProperty] private string _body = "";
    [ObservableProperty] private bool _isEdited;
    [ObservableProperty] private bool _isDeleted;
    /// <summary>"" (sent, no confirmation yet), "Delivered", "Read", or "Viewed".</summary>
    [ObservableProperty] private string _receiptStatus = "";

    public bool IsOutgoing { get; init; }
    public bool IsViewOnce { get; init; }
    public DateTimeOffset SentAt { get; init; }

    public ObservableCollection<AttachmentItem> Attachments { get; } = new();
    public ObservableCollection<ReactionItem> Reactions { get; } = new();
    public QuoteItem? Quote { get; init; }
    public PreviewItem? Preview { get; init; }

    public ChatMessage()
    {
        Reactions.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasReactions));
            OnPropertyChanged(nameof(ReactionsVisibility));
        };
        Attachments.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAttachment));
            OnPropertyChanged(nameof(AttachmentVisibility));
        };
    }

    public bool HasBody => !IsDeleted && !string.IsNullOrEmpty(Body);
    public bool HasAttachment => !IsDeleted && Attachments.Count > 0;
    public bool HasQuote => !IsDeleted && Quote != null;
    public bool HasPreview => !IsDeleted && Preview != null;
    public bool HasReactions => !IsDeleted && Reactions.Count > 0;
    public string TimeDisplay => SentAt.ToLocalTime().ToString("HH:mm");
    public string DeletedDisplay => "This message was deleted";

    public Visibility BodyVisibility => HasBody ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AttachmentVisibility => HasAttachment ? Visibility.Visible : Visibility.Collapsed;
    public Visibility QuoteVisibility => HasQuote ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PreviewVisibility => HasPreview ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ReactionsVisibility => HasReactions ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DeletedVisibility => IsDeleted ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EditedVisibility => IsEdited && !IsDeleted ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DeleteMenuVisibility => IsOutgoing && !IsDeleted ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ReceiptVisibility =>
        IsOutgoing && !IsDeleted && ReceiptStatus.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string ReceiptGlyph => ReceiptStatus switch
    {
        "Read" or "Viewed" => "✓✓",
        "Delivered" => "✓",
        _ => "",
    };

    public HorizontalAlignment Alignment =>
        IsOutgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    // Sent bubbles follow the user's system accent color (a darker shade for
    // contrast with white text), received bubbles stay a neutral dark surface
    // -- same relationship Signal/Flare use, adapted to WinUI resource keys.
    public Brush BubbleBrush
    {
        get
        {
            if (!IsOutgoing) return new SolidColorBrush(Color.FromArgb(255, 0x2A, 0x2A, 0x2E));
            if (Application.Current.Resources.TryGetValue("SystemAccentColorDark1", out var res) && res is Color accent)
                return new SolidColorBrush(accent);
            return new SolidColorBrush(Color.FromArgb(255, 0x2B, 0x5C, 0xB8));
        }
    }

    partial void OnBodyChanged(string value)
    {
        OnPropertyChanged(nameof(HasBody));
        OnPropertyChanged(nameof(BodyVisibility));
    }

    partial void OnReceiptStatusChanged(string value)
    {
        OnPropertyChanged(nameof(ReceiptVisibility));
        OnPropertyChanged(nameof(ReceiptGlyph));
    }

    partial void OnIsDeletedChanged(bool value)
    {
        OnPropertyChanged(nameof(HasBody));
        OnPropertyChanged(nameof(HasAttachment));
        OnPropertyChanged(nameof(HasQuote));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(HasReactions));
        OnPropertyChanged(nameof(BodyVisibility));
        OnPropertyChanged(nameof(AttachmentVisibility));
        OnPropertyChanged(nameof(QuoteVisibility));
        OnPropertyChanged(nameof(PreviewVisibility));
        OnPropertyChanged(nameof(ReactionsVisibility));
        OnPropertyChanged(nameof(DeletedVisibility));
    }
}
