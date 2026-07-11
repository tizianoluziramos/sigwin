using System;
using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
using Windows.Storage.Streams;

namespace signal_windows_client.ViewModels;

/// <summary>Backs the linking screen: renders the provisioning URI as a QR.</summary>
public sealed partial class LinkingViewModel : ObservableObject
{
    [ObservableProperty] private ImageSource? _qrImage;
    [ObservableProperty] private bool _hasQr;
    [ObservableProperty] private string _statusText = "Starting…";

    public async void ShowQr(string url)
    {
        StatusText = "Scan this code to link Sigwin";

        var generator = new QRCodeGenerator();
        var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        byte[] png = new PngByteQRCode(data).GetGraphic(12);

        var bitmap = new BitmapImage();
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(png.AsBuffer());
        stream.Seek(0);
        await bitmap.SetSourceAsync(stream);

        QrImage = bitmap;
        HasQr = true;
    }

    public void Reset()
    {
        QrImage = null;
        HasQr = false;
        StatusText = "Starting…";
    }
}
