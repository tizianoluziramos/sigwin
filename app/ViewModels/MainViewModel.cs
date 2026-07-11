using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using signal_windows_client.Interop;
using signal_windows_client.Models;
using Windows.Storage.Streams;

namespace signal_windows_client.ViewModels;

/// <summary>Drives the chat shell: conversation list + selected thread.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    public ObservableCollection<Conversation> Conversations { get; } = new();
    private readonly Dictionary<string, Conversation> _byId = new();
    // Keyed by (threadId, original send timestamp) -- the stable identity
    // Signal uses for edits/deletes/reactions to point back at a message.
    private readonly Dictionary<(string ThreadId, long Timestamp), ChatMessage> _messagesByKey = new();
    // Receipts only carry a timestamp (no thread id) -- they're addressed to
    // whichever of our own messages has that timestamp, globally unique since
    // we generate it from our own wall clock.
    private readonly Dictionary<long, ChatMessage> _outgoingByTimestamp = new();

    private bool _typingActive;
    private Conversation? _typingConversation;
    private readonly System.Timers.Timer _typingStopTimer = new(3000) { AutoReset = false };

    [ObservableProperty] private Conversation? _selectedConversation;
    [ObservableProperty] private string _statusText = "Connecting…";
    [ObservableProperty] private string _draft = "";
    [ObservableProperty] private ChatMessage? _replyingTo;

    public bool HasSelection => SelectedConversation != null;
    public bool HasReply => ReplyingTo != null;

    public MainViewModel()
    {
        _typingStopTimer.Elapsed += (_, _) =>
        {
            if (_typingConversation is { } c) App.Signal.SendTyping(c.Id, false);
            _typingActive = false;
        };
    }

    partial void OnSelectedConversationChanged(Conversation? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        ReplyingTo = null;
        if (value != null) SendReadReceiptForLatest(value);
    }

    partial void OnReplyingToChanged(ChatMessage? value) => OnPropertyChanged(nameof(HasReply));

    // Sends a "started"/"stopped" typing notification as the draft changes,
    // debounced so "stopped" only fires ~3s after the user pauses typing.
    // 1:1 threads only for now -- see the core-side comment on send_typing.
    partial void OnDraftChanged(string value)
    {
        var convo = SelectedConversation;
        if (convo is null || convo.IsGroup) return;

        if (string.IsNullOrEmpty(value))
        {
            _typingStopTimer.Stop();
            if (_typingActive) App.Signal.SendTyping(convo.Id, false);
            _typingActive = false;
            return;
        }

        _typingConversation = convo;
        if (!_typingActive)
        {
            _typingActive = true;
            App.Signal.SendTyping(convo.Id, true);
        }
        _typingStopTimer.Stop();
        _typingStopTimer.Start();
    }

    /// <summary>Send the current draft (as a reply if one is set) to the selected conversation.</summary>
    public void SendDraft()
    {
        var convo = SelectedConversation;
        var text = Draft?.Trim() ?? "";
        if (convo is null || text.Length == 0) return;

        var reply = ReplyingTo;
        App.Signal.SendText(convo.Id, text, reply?.Timestamp ?? 0, reply?.SenderUuid, reply?.Body);
        Draft = "";
        ReplyingTo = null;
    }

    private void SendReadReceiptForLatest(Conversation convo)
    {
        for (int i = convo.Messages.Count - 1; i >= 0; i--)
        {
            var msg = convo.Messages[i];
            if (!msg.IsOutgoing && !string.IsNullOrEmpty(msg.SenderUuid))
            {
                App.Signal.SendReadReceipt(msg.SenderUuid, msg.Timestamp);
                return;
            }
        }
    }

    /// <summary>Reply to a specific message: shows a quote banner above the composer.</summary>
    public void SetReply(ChatMessage msg) => ReplyingTo = msg;
    public void CancelReply() => ReplyingTo = null;

    /// <summary>Add (or remove) an emoji reaction on a message.</summary>
    public void React(ChatMessage msg, string emoji, bool remove = false)
    {
        var convo = SelectedConversation;
        if (convo is null) return;
        App.Signal.SendReaction(convo.Id, msg.Timestamp, msg.SenderUuid, emoji, remove);
        ApplyReaction(convo.Id, msg.Timestamp, emoji, remove);
    }

    /// <summary>Delete one of your own messages for everyone.</summary>
    public void DeleteMessage(ChatMessage msg)
    {
        var convo = SelectedConversation;
        if (convo is null || !msg.IsOutgoing) return;
        App.Signal.DeleteMessage(convo.Id, msg.Timestamp);
        msg.IsDeleted = true;
    }

    /// <summary>Upload and send an image/video attachment to the selected conversation.</summary>
    public void SendAttachment(string contentType, byte[] data, string? fileName = null, string? caption = null)
    {
        var convo = SelectedConversation;
        if (convo is null || data.Length == 0) return;
        App.Signal.SendAttachment(convo.Id, contentType, Convert.ToBase64String(data), caption, fileName);
    }

    /// <summary>Handle an event from the core (already on the UI thread).</summary>
    public void OnEvent(SignalEvent ev)
    {
        switch (ev)
        {
            case ConversationReceived c:
                UpsertConversation(c.Info);
                break;
            case ConversationAvatarReceived a:
                _ = SetAvatarAsync(a.ThreadId, a.AvatarBase64);
                break;
            case MessageReceived m:
                AddMessage(m.Message);
                break;
            case MessageEditedReceived e:
                ApplyEdit(e);
                break;
            case MessageDeletedReceived d:
                if (_messagesByKey.TryGetValue((d.ThreadId, d.TargetTimestamp), out var deleted))
                    deleted.IsDeleted = true;
                break;
            case ReactionReceived r:
                ApplyReaction(r.ThreadId, r.TargetTimestamp, r.Emoji, r.Remove);
                break;
            case TypingReceived t:
                if (_byId.TryGetValue(t.ThreadId, out var typingConvo)) typingConvo.IsTyping = t.IsTyping;
                break;
            case ReceiptReceived rc:
                foreach (var ts in rc.Timestamps)
                    if (_outgoingByTimestamp.TryGetValue(ts, out var receiptMsg))
                        ApplyReceiptStatus(receiptMsg, rc.ReceiptType);
                break;
            case QueueDrained:
                StatusText = "Connected";
                break;
        }
    }

    private Conversation GetOrCreate(string threadId, string title)
    {
        if (!_byId.TryGetValue(threadId, out var convo))
        {
            convo = new Conversation(threadId, title);
            _byId[threadId] = convo;
            Conversations.Add(convo);
        }
        return convo;
    }

    private void UpsertConversation(ConversationDto info)
    {
        var convo = GetOrCreate(info.ThreadId, info.Title);
        convo.IsGroup = info.IsGroup;
        if (!string.IsNullOrEmpty(info.Title) && ShouldReplaceTitle(convo.Title, info.Title))
            convo.Title = info.Title;
        if (info.LastTimestamp > 0)
        {
            convo.LastPreview = info.LastBody ?? "";
            convo.LastTime = DateTimeOffset.FromUnixTimeMilliseconds(info.LastTimestamp);
        }
        PlaceSorted(convo);
    }

    private void AddMessage(IncomingMessageDto dto)
    {
        var convo = GetOrCreate(dto.ThreadId, FallbackTitle(dto.Sender));

        QuoteItem? quote = dto.Quote is { } q
            ? new QuoteItem { Timestamp = q.Timestamp, Author = q.Author, Text = q.Text, AuthorDisplay = ResolveAuthorDisplay(q.Author) }
            : null;

        PreviewItem? preview = dto.Preview is { } p
            ? new PreviewItem { Url = p.Url, Title = p.Title, Description = p.Description }
            : null;

        var msg = new ChatMessage
        {
            ThreadId = dto.ThreadId,
            SenderUuid = dto.Sender,
            Timestamp = dto.Timestamp,
            Body = dto.Body ?? "",
            SentAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.Timestamp),
            IsOutgoing = dto.IsOutgoing,
            IsViewOnce = dto.IsViewOnce,
            Quote = quote,
            Preview = preview,
        };

        foreach (var a in dto.Attachments)
        {
            var item = new AttachmentItem { ContentType = a.ContentType, FileName = a.FileName, Size = a.Size };
            msg.Attachments.Add(item);
            if (!string.IsNullOrEmpty(a.DataBase64)) _ = LoadAttachmentDataAsync(item, a.DataBase64);
        }

        _messagesByKey[(dto.ThreadId, dto.Timestamp)] = msg;
        if (dto.IsOutgoing) _outgoingByTimestamp[dto.Timestamp] = msg;
        convo.Messages.Add(msg);
        convo.LastPreview = LastPreviewText(msg);
        convo.LastTime = msg.SentAt;

        // Historical messages arrive already ordered under their conversation, so
        // only live messages need to bubble the conversation up the list.
        if (!dto.Historical) PlaceSorted(convo);

        if (preview is { } pv && dto.Preview?.ImageBase64 is { Length: > 0 } imgB64)
            _ = LoadPreviewImageAsync(pv, imgB64);
    }

    private void ApplyEdit(MessageEditedReceived e)
    {
        if (!_messagesByKey.TryGetValue((e.ThreadId, e.TargetTimestamp), out var msg)) return;
        msg.Body = e.NewBody ?? "";
        msg.IsEdited = true;
        // Re-key under the edit's own timestamp too, in case a later edit or
        // reaction targets that instead of the original.
        _messagesByKey[(e.ThreadId, e.NewTimestamp)] = msg;
    }

    private void ApplyReaction(string threadId, long targetTimestamp, string emoji, bool remove)
    {
        if (!_messagesByKey.TryGetValue((threadId, targetTimestamp), out var msg)) return;
        var existing = msg.Reactions.FirstOrDefault(x => x.Emoji == emoji);
        if (remove)
        {
            if (existing is null) return;
            existing.Count--;
            if (existing.Count <= 0) msg.Reactions.Remove(existing);
        }
        else if (existing is not null)
        {
            existing.Count++;
        }
        else
        {
            msg.Reactions.Add(new ReactionItem { Emoji = emoji, Count = 1 });
        }
    }

    // Read > Viewed > Delivered in confirmation strength -- never let a later,
    // weaker receipt (e.g. a stale Delivery retry) downgrade the display.
    private static readonly string[] ReceiptRank = { "", "Delivered", "Viewed", "Read" };

    private static void ApplyReceiptStatus(ChatMessage msg, string receiptType)
    {
        int incoming = Array.IndexOf(ReceiptRank, receiptType);
        int current = Array.IndexOf(ReceiptRank, msg.ReceiptStatus);
        if (incoming > current) msg.ReceiptStatus = receiptType;
    }

    private static string LastPreviewText(ChatMessage msg)
    {
        if (msg.HasBody) return msg.Body;
        if (msg.Attachments.Count > 0) return "\U0001F4CE Attachment";
        return "";
    }

    private string ResolveAuthorDisplay(string authorUuid)
    {
        if (_byId.TryGetValue($"contact:{authorUuid}", out var c)) return c.Title;
        return authorUuid.Length >= 8 ? authorUuid[..8] : authorUuid;
    }

    /// <summary>
    /// Keep the list ordered by most-recent activity (newest first) using a
    /// single Move (no remove/insert), so the ListView doesn't flicker or drop
    /// selection.
    /// </summary>
    private void PlaceSorted(Conversation convo)
    {
        int current = Conversations.IndexOf(convo);
        if (current < 0) return;

        int target = 0;
        foreach (var c in Conversations)
        {
            if (!ReferenceEquals(c, convo) && c.LastTime > convo.LastTime) target++;
        }

        if (target == current) return;

        // ListView can clear SelectedItem as a side effect of a bound
        // collection Move; restore it so sending a message doesn't kick the
        // user back to the empty state.
        bool wasSelected = ReferenceEquals(SelectedConversation, convo);
        Conversations.Move(current, target);
        if (wasSelected) SelectedConversation = convo;
    }

    /// <summary>Clear all state (used on log-out).</summary>
    public void Reset()
    {
        SelectedConversation = null;
        Conversations.Clear();
        _byId.Clear();
        _messagesByKey.Clear();
        _outgoingByTimestamp.Clear();
        StatusText = "Connecting…";
    }

    // Don't let a raw-UUID fallback title overwrite an already-resolved name.
    private static bool ShouldReplaceTitle(string current, string incoming)
    {
        if (!Guid.TryParse(incoming, out _)) return true; // a real name always wins
        return string.IsNullOrEmpty(current) || Guid.TryParse(current, out _);
    }

    private async Task SetAvatarAsync(string threadId, string base64)
    {
        if (string.IsNullOrEmpty(base64) || !_byId.TryGetValue(threadId, out var convo)) return;
        try
        {
            var bytes = Convert.FromBase64String(base64);
            var bitmap = new BitmapImage();
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);
            await bitmap.SetSourceAsync(stream);
            convo.Avatar = bitmap;
        }
        catch { /* ignore malformed avatar data */ }
    }

    private async Task LoadAttachmentDataAsync(AttachmentItem item, string base64)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64);
            if (item.IsImage)
            {
                var bitmap = new BitmapImage();
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(bytes.AsBuffer());
                stream.Seek(0);
                await bitmap.SetSourceAsync(stream);
                item.Image = bitmap;
            }
            else if (item.IsVideo)
            {
                // MediaPlayerElement wants a file/URI, not raw bytes -- write
                // the decrypted attachment out to a temp file once.
                var ext = item.ContentType switch
                {
                    "video/mp4" => ".mp4",
                    "video/quicktime" => ".mov",
                    "video/webm" => ".webm",
                    _ => ".bin",
                };
                var path = Path.Combine(Path.GetTempPath(), $"sigwin_{Guid.NewGuid():N}{ext}");
                await File.WriteAllBytesAsync(path, bytes);
                item.VideoPath = path;
            }
        }
        catch { /* ignore malformed attachment data */ }
    }

    private async Task LoadPreviewImageAsync(PreviewItem preview, string base64)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64);
            var bitmap = new BitmapImage();
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);
            await bitmap.SetSourceAsync(stream);
            preview.Image = bitmap;
        }
        catch { /* ignore malformed preview image */ }
    }

    // Until group sender names resolve, derive a short label from the sender's uuid.
    private static string FallbackTitle(string senderUuid) =>
        senderUuid.Length >= 8 ? $"Contact {senderUuid[..8]}" : $"Contact {senderUuid}";
}
