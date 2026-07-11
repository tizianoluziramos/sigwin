using System;
using Microsoft.UI.Xaml.Data;
using Windows.Media.Core;

namespace signal_windows_client.Converters;

/// <summary>Turns a local temp-file path (written from decrypted attachment
/// bytes) into a playback source for <c>MediaPlayerElement</c>.</summary>
public sealed class VideoPathToMediaSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string path && !string.IsNullOrEmpty(path))
        {
            try { return MediaSource.CreateFromUri(new Uri(path)); }
            catch { return null; }
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
