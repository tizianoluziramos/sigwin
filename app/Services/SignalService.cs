using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;
using signal_windows_client.Interop;
using Sigwin.Interop;

namespace signal_windows_client.Services;

/// <summary>
/// Managed wrapper over the Rust core's C ABI. Owns the native runtime handle,
/// routes the unmanaged callbacks back to this instance (via a GCHandle passed
/// as <c>user_data</c>), and marshals every callback onto the UI thread through
/// the captured <see cref="DispatcherQueue"/> before raising managed events.
/// </summary>
public sealed unsafe class SignalService
{
    private readonly DispatcherQueue _ui;
    private GCHandle _self;
    private SigwinHandle* _handle;

    /// <summary>Fires (on the UI thread) with the provisioning URI when linking is required.</summary>
    public event Action<string>? ProvisioningUrlReceived;

    /// <summary>Fires (on the UI thread) for each incoming event.</summary>
    public event Action<SignalEvent>? EventReceived;

    public SignalService(DispatcherQueue ui) => _ui = ui;

    public bool IsRunning => _handle != null;

    public void Start(string dbPath, string passphrase, string deviceName)
    {
        if (_handle != null) return;

        _self = GCHandle.Alloc(this);
        byte* pDb = Utf8(dbPath);
        byte* pPass = Utf8(passphrase);
        byte* pDev = Utf8(deviceName);
        try
        {
            // sigwin_start reads the strings synchronously before returning, so
            // the buffers can be freed immediately after.
            _handle = NativeMethods.sigwin_start(
                pDb, pPass, pDev, &OnUrl, &OnEvent, (void*)GCHandle.ToIntPtr(_self));
        }
        finally
        {
            Marshal.FreeHGlobal((IntPtr)pDb);
            Marshal.FreeHGlobal((IntPtr)pPass);
            Marshal.FreeHGlobal((IntPtr)pDev);
        }

        if (_handle == null)
        {
            _self.Free();
            throw new InvalidOperationException("sigwin_start failed (invalid arguments).");
        }
    }

    public void Stop()
    {
        if (_handle != null)
        {
            NativeMethods.sigwin_stop(_handle);
            _handle = null;
        }
        if (_self.IsAllocated) _self.Free();
    }

    /// <summary>Queue a text message to a conversation thread, optionally as a reply.</summary>
    public void SendText(string threadId, string text, long quoteTimestamp = 0, string? quoteAuthor = null, string? quoteText = null)
    {
        if (_handle == null) return;
        byte* pThread = Utf8(threadId);
        byte* pText = Utf8(text);
        byte* pQuoteAuthor = UtfOrNull(quoteAuthor);
        byte* pQuoteText = UtfOrNull(quoteText);
        try
        {
            NativeMethods.sigwin_send_text(_handle, pThread, pText, quoteTimestamp, pQuoteAuthor, pQuoteText);
        }
        finally
        {
            Marshal.FreeHGlobal((IntPtr)pThread);
            Marshal.FreeHGlobal((IntPtr)pText);
            if (pQuoteAuthor != null) Marshal.FreeHGlobal((IntPtr)pQuoteAuthor);
            if (pQuoteText != null) Marshal.FreeHGlobal((IntPtr)pQuoteText);
        }
    }

    /// <summary>Queue an image/video attachment (base64) with an optional caption.</summary>
    public void SendAttachment(string threadId, string contentType, string dataBase64, string? caption = null, string? fileName = null)
    {
        if (_handle == null) return;
        byte* pThread = Utf8(threadId);
        byte* pCaption = UtfOrNull(caption);
        byte* pContentType = Utf8(contentType);
        byte* pFileName = UtfOrNull(fileName);
        byte* pData = Utf8(dataBase64);
        try
        {
            NativeMethods.sigwin_send_attachment(_handle, pThread, pCaption, pContentType, pFileName, pData);
        }
        finally
        {
            Marshal.FreeHGlobal((IntPtr)pThread);
            if (pCaption != null) Marshal.FreeHGlobal((IntPtr)pCaption);
            Marshal.FreeHGlobal((IntPtr)pContentType);
            if (pFileName != null) Marshal.FreeHGlobal((IntPtr)pFileName);
            Marshal.FreeHGlobal((IntPtr)pData);
        }
    }

    /// <summary>Queue an emoji reaction (or its removal) on a message.</summary>
    public void SendReaction(string threadId, long targetTimestamp, string targetAuthor, string emoji, bool remove = false)
    {
        if (_handle == null) return;
        byte* pThread = Utf8(threadId);
        byte* pAuthor = Utf8(targetAuthor);
        byte* pEmoji = Utf8(emoji);
        try
        {
            NativeMethods.sigwin_send_reaction(_handle, pThread, targetTimestamp, pAuthor, pEmoji, remove);
        }
        finally
        {
            Marshal.FreeHGlobal((IntPtr)pThread);
            Marshal.FreeHGlobal((IntPtr)pAuthor);
            Marshal.FreeHGlobal((IntPtr)pEmoji);
        }
    }

    /// <summary>Queue a "delete for everyone" for a message this device sent.</summary>
    public void DeleteMessage(string threadId, long targetTimestamp)
    {
        if (_handle == null) return;
        byte* pThread = Utf8(threadId);
        try
        {
            NativeMethods.sigwin_delete_message(_handle, pThread, targetTimestamp);
        }
        finally
        {
            Marshal.FreeHGlobal((IntPtr)pThread);
        }
    }

    /// <summary>Queue a typing-started/stopped notification (1:1 threads only).</summary>
    public void SendTyping(string threadId, bool typing)
    {
        if (_handle == null) return;
        byte* pThread = Utf8(threadId);
        try
        {
            NativeMethods.sigwin_send_typing(_handle, pThread, typing);
        }
        finally
        {
            Marshal.FreeHGlobal((IntPtr)pThread);
        }
    }

    /// <summary>Queue a read receipt directly to a message's original sender.</summary>
    public void SendReadReceipt(string targetAuthor, long timestamp)
    {
        if (_handle == null) return;
        byte* pAuthor = Utf8(targetAuthor);
        try
        {
            NativeMethods.sigwin_send_read_receipt(_handle, pAuthor, timestamp);
        }
        finally
        {
            Marshal.FreeHGlobal((IntPtr)pAuthor);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnUrl(byte* url, void* user)
    {
        var svc = (SignalService)GCHandle.FromIntPtr((IntPtr)user).Target!;
        string s = Marshal.PtrToStringUTF8((IntPtr)url) ?? "";
        svc._ui.TryEnqueue(() => svc.ProvisioningUrlReceived?.Invoke(s));
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnEvent(byte* json, void* user)
    {
        var svc = (SignalService)GCHandle.FromIntPtr((IntPtr)user).Target!;
        string s = Marshal.PtrToStringUTF8((IntPtr)json) ?? "";
        var ev = SignalEvent.Parse(s);
        svc._ui.TryEnqueue(() => svc.EventReceived?.Invoke(ev));
    }

    private static byte* Utf8(string s)
    {
        int len = Encoding.UTF8.GetByteCount(s);
        var p = (byte*)Marshal.AllocHGlobal(len + 1);
        fixed (char* c = s)
        {
            Encoding.UTF8.GetBytes(c, s.Length, p, len);
        }
        p[len] = 0;
        return p;
    }

    private static byte* UtfOrNull(string? s) => s is null ? null : Utf8(s);
}
