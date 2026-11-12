using System.Drawing;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace v2rayN.Common;

internal static partial class WindowsUtils
{
    private static readonly string _tag = "WindowsUtils";

    public static string? GetClipboardData()
    {
        var strData = string.Empty;
        try
        {
            var data = Clipboard.GetDataObject();
            if (data?.GetDataPresent(DataFormats.UnicodeText) == true)
            {
                strData = data.GetData(DataFormats.UnicodeText)?.ToString();
            }
            return strData;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        return strData;
    }

    public static void SetClipboardData(string strData)
    {
        try
        {
            Clipboard.SetText(strData);
        }
        catch
        {
        }
    }

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, DWMWINDOWATTRIBUTE attribute, ref int attributeValue, uint attributeSize);

    public static ImageSource IconToImageSource(Icon icon)
    {
        return Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            new Int32Rect(0, 0, icon.Width, icon.Height),
            BitmapSizeOptions.FromEmptyOptions());
    }

    public static void SetDarkBorder(Window window, string? theme)
    {
        var isDark = theme switch
        {
            nameof(ETheme.Dark) => true,
            nameof(ETheme.Light) => false,
            _ => IsDarkTheme(),
        };

        SetDarkBorder(window, isDark);
    }

    private static void SetDarkBorder(Window window, bool dark)
    {
        // Make sure the handle is created before the window is shown
        var hWnd = new WindowInteropHelper(window).EnsureHandle();
        var attribute = dark ? 1 : 0;
        var attributeSize = (uint)Marshal.SizeOf(attribute);
        DwmSetWindowAttribute(hWnd, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref attribute, attributeSize);
        DwmSetWindowAttribute(hWnd, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, ref attribute, attributeSize);

        // ProSyS: paint the native title bar in the app's background colour, even when Windows
        // "show accent colour on title bars" is on (Windows 11+, ignored on older builds).
        if (dark)
        {
            var caption = 0x00060705; // COLORREF (0x00BBGGRR) for #050706
            var text = 0x00F3F6F2;    // #F2F6F3
            var border = 0x00121A14;  // #141A12
            DwmSetWindowAttribute(hWnd, DWMWINDOWATTRIBUTE.DWMWA_CAPTION_COLOR, ref caption, attributeSize);
            DwmSetWindowAttribute(hWnd, DWMWINDOWATTRIBUTE.DWMWA_TEXT_COLOR, ref text, attributeSize);
            DwmSetWindowAttribute(hWnd, DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR, ref border, attributeSize);
        }
    }

    private static bool IsDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var obj = key?.GetValue("AppsUseLightTheme");
        var value = obj?.ToString().ToInt();
        return value == 0;
    }

    #region Windows API

    [Flags]
    public enum DWMWINDOWATTRIBUTE : uint
    {
        DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19,
        DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
        DWMWA_BORDER_COLOR = 34,
        DWMWA_CAPTION_COLOR = 35,
        DWMWA_TEXT_COLOR = 36,
    }

    #endregion Windows API
}
