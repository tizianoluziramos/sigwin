using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using signal_windows_client.Models;
using signal_windows_client.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace signal_windows_client.Views;

public sealed partial class MainShellPage : Page
{
    public MainViewModel Vm => App.MainVm;

    private ObservableCollection<ChatMessage>? _observed;

    public MainShellPage()
    {
        InitializeComponent();
        Vm.PropertyChanged += OnVmPropertyChanged;
    }

    // x:Bind function helpers.
    public Visibility Vis(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VisInv(bool b) => b ? Visibility.Collapsed : Visibility.Visible;

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.SelectedConversation)) return;

        if (_observed != null) _observed.CollectionChanged -= OnMessagesChanged;
        _observed = Vm.SelectedConversation?.Messages;
        if (_observed != null) _observed.CollectionChanged += OnMessagesChanged;
        ScrollToBottom();

        if (Vm.SelectedConversation != null) Composer.Focus(FocusState.Programmatic);
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToBottom();

    private void ScrollToBottom()
    {
        if (_observed is { Count: > 0 } msgs)
        {
            MessagesList.ScrollIntoView(msgs[msgs.Count - 1]);
        }
    }

    private void OnSend(object sender, RoutedEventArgs e) => Vm.SendDraft();

    private void OnComposerKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            Vm.SendDraft();
            e.Handled = true;
        }
    }

    private void OnReplyClick(object sender, RoutedEventArgs e)
    {
        if (((MenuFlyoutItem)sender).Tag is ChatMessage msg) Vm.SetReply(msg);
    }

    private void OnCancelReplyClick(object sender, RoutedEventArgs e) => Vm.CancelReply();

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (((MenuFlyoutItem)sender).Tag is not ChatMessage { Body.Length: > 0 } msg) return;
        var pkg = new DataPackage();
        pkg.SetText(msg.Body);
        Clipboard.SetContent(pkg);
    }

    private void OnReactClick(object sender, RoutedEventArgs e)
    {
        var item = (MenuFlyoutItem)sender;
        if (item.Tag is ChatMessage msg) Vm.React(msg, item.Text);
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (((MenuFlyoutItem)sender).Tag is not ChatMessage msg) return;
        var dialog = new ContentDialog
        {
            Title = "Delete for everyone?",
            Content = "This removes the message for everyone in the conversation.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) Vm.DeleteMessage(msg);
    }

    private async void OnAttachClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".mp4", ".mov", ".webm" })
            picker.FileTypeFilter.Add(ext);
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file != null) await SendFileAsync(file);
    }

    private void OnComposerDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void OnComposerDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        foreach (var item in items)
        {
            if (item is StorageFile file) await SendFileAsync(file);
        }
    }

    private async void OnComposerPaste(object sender, TextControlPasteEventArgs e)
    {
        var view = Clipboard.GetContent();
        if (!view.Contains(StandardDataFormats.Bitmap)) return;

        e.Handled = true;
        try
        {
            var streamRef = await view.GetBitmapAsync();
            using var stream = await streamRef.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.AsStreamForRead().CopyToAsync(ms);
            Vm.SendAttachment("image/png", ms.ToArray());
        }
        catch { /* clipboard bitmap couldn't be read -- nothing to send */ }
    }

    private async Task SendFileAsync(StorageFile file)
    {
        byte[] bytes;
        using (var stream = await file.OpenReadAsync())
        using (var ms = new MemoryStream())
        {
            await stream.AsStreamForRead().CopyToAsync(ms);
            bytes = ms.ToArray();
        }

        var contentType = file.ContentType;
        if (string.IsNullOrEmpty(contentType) || contentType == "application/octet-stream")
            contentType = GuessContentType(file.FileType);

        Vm.SendAttachment(contentType, bytes, file.Name);
    }

    private static string GuessContentType(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        ".webm" => "video/webm",
        _ => "application/octet-stream",
    };

    private async void OnLogOut(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Log out?",
            Content = "This unlinks Sigwin from your phone and clears local data. "
                    + "You'll scan the QR code again to re-link.",
            PrimaryButtonText = "Log out",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            App.LogOut();
        }
    }
}
