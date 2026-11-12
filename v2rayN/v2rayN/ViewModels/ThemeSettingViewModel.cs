using MaterialDesignColors;
using MaterialDesignColors.ColorManipulation;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;

namespace v2rayN.ViewModels;

public partial class ThemeSettingViewModel : MyReactiveObject
{
    private readonly PaletteHelper _paletteHelper = new();

    public BulkObservableCollection<Swatch> Swatches { get; } = [];

    [Reactive]
    public partial Swatch SelectedSwatch { get; set; }

    [Reactive] public partial string CurrentTheme { get; set; }

    [Reactive] public partial int CurrentFontSize { get; set; }

    [Reactive] public partial string CurrentLanguage { get; set; }

    public ThemeSettingViewModel()
    {
        _config = AppManager.Instance.Config;

        RegisterSystemColorSet(_config, ModifyTheme);

        BindingUI();
        RestoreUI();
    }

    private void RestoreUI()
    {
        ModifyTheme();
        ModifyFontSize();
    }

    private void BindingUI()
    {
        Swatches.AddRange(new SwatchesProvider().Swatches);
        if (!_config.UiItem.ColorPrimaryName.IsNullOrEmpty())
        {
            SelectedSwatch = Swatches.FirstOrDefault(t => t.Name == _config.UiItem.ColorPrimaryName);
        }
        CurrentTheme = _config.UiItem.CurrentTheme;
        CurrentFontSize = _config.UiItem.CurrentFontSize;
        CurrentLanguage = _config.UiItem.CurrentLanguage;

        this.WhenAnyValue(x => x.CurrentTheme)
            .Where(y => y != null && !y.IsNullOrEmpty())
            .SubscribeAsync(async _ =>
             {
                 if (_config.UiItem.CurrentTheme != CurrentTheme)
                 {
                     _config.UiItem.CurrentTheme = CurrentTheme;
                     ModifyTheme();
                     await ConfigHandler.SaveConfig(_config);
                 }
             });

        this.WhenAnyValue(x => x.SelectedSwatch)
             .Where(y => y != null && !y.Name.IsNullOrEmpty())
             .SubscribeAsync(async _ =>
             {
                 if (SelectedSwatch == null
                 || SelectedSwatch.Name.IsNullOrEmpty()
                 || SelectedSwatch.ExemplarHue == null
                 || SelectedSwatch.ExemplarHue?.Color == null)
                 {
                     return;
                 }
                 if (_config.UiItem.ColorPrimaryName != SelectedSwatch?.Name)
                 {
                     _config.UiItem.ColorPrimaryName = SelectedSwatch?.Name;
                     ChangePrimaryColor(SelectedSwatch.ExemplarHue.Color);
                     await ConfigHandler.SaveConfig(_config);
                 }
             });

        this.WhenAnyValue(x => x.CurrentFontSize)
              .Where(y => y > 0)
              .SubscribeAsync(async _ =>
              {
                  if (_config.UiItem.CurrentFontSize != CurrentFontSize)
                  {
                      _config.UiItem.CurrentFontSize = CurrentFontSize;
                      ModifyFontSize();
                      await ConfigHandler.SaveConfig(_config);
                  }
              });

        this.WhenAnyValue(x => x.CurrentLanguage)
            .Where(y => y != null && !y.IsNullOrEmpty())
            .SubscribeAsync(async _ =>
            {
                if (CurrentLanguage.IsNotEmpty() && _config.UiItem.CurrentLanguage != CurrentLanguage)
                {
                    _config.UiItem.CurrentLanguage = CurrentLanguage;
                    Thread.CurrentThread.CurrentUICulture = new(CurrentLanguage);
                    await ConfigHandler.SaveConfig(_config);
                    NoticeManager.Instance.Enqueue(ResUI.NeedRebootTips);
                }
            });
    }

    /// <summary>ProSyS: the app always uses the dark neon palette of the website and Android app.</summary>
    public void ModifyTheme()
    {
        var theme = _paletteHelper.GetTheme();
        theme.SetBaseTheme(BaseTheme.Dark);
        var acid = System.Windows.Media.Color.FromRgb(0xB8, 0xFF, 0x4D);
        var acidText = System.Windows.Media.Color.FromRgb(0x07, 0x11, 0x09);
        var mint = System.Windows.Media.Color.FromRgb(0x63, 0xFF, 0xBA);
        theme.PrimaryLight = new ColorPair(acid.Lighten(), acidText);
        theme.PrimaryMid = new ColorPair(acid, acidText);
        theme.PrimaryDark = new ColorPair(System.Windows.Media.Color.FromRgb(0x8C, 0xEE, 0x47), acidText);
        theme.SecondaryLight = new ColorPair(mint.Lighten(), acidText);
        theme.SecondaryMid = new ColorPair(mint, acidText);
        theme.SecondaryDark = new ColorPair(mint.Darken(), acidText);
        _paletteHelper.SetTheme(theme);

        ApplyProSySSurfaces();
        WindowsUtils.SetDarkBorder(Application.Current.MainWindow, nameof(ETheme.Dark));
    }

    private static void ApplyProSySSurfaces()
    {
        static System.Windows.Media.SolidColorBrush B(uint argb)
        {
            // The project builds with CheckForOverflowUnderflow, so mask each channel instead of narrowing a uint.
            var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(
                (byte)((argb >> 24) & 0xFF), (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF)));
            brush.Freeze();
            return brush;
        }
        var res = Application.Current.Resources;
        var bg = B(0xFF050706);
        var surface = B(0xFF0B0F0C);
        var surface2 = B(0xFF101712);
        var ink = B(0xFFF2F6F3);
        var line = B(0x1AFFFFFF);
        foreach (var key in new[] { "MaterialDesign.Brush.Background", "MaterialDesignPaper", "MaterialDesignBackground" })
        {
            res[key] = bg;
        }
        foreach (var key in new[] { "MaterialDesign.Brush.Card.Background", "MaterialDesignCardBackground", "MaterialDesign.Brush.ComboBox.DropDown.Background" })
        {
            res[key] = surface2;
        }
        foreach (var key in new[] { "MaterialDesign.Brush.ToolBar.Background", "MaterialDesignToolBarBackground" })
        {
            res[key] = surface;
        }
        foreach (var key in new[] { "MaterialDesign.Brush.Foreground", "MaterialDesignBody" })
        {
            res[key] = ink;
        }
        foreach (var key in new[] { "MaterialDesign.Brush.Card.Border", "MaterialDesign.Brush.Separator.Background", "MaterialDesign.Brush.ListView.Separator", "MaterialDesign.Brush.DataGrid.Border", "MaterialDesign.Brush.ToolBar.Separator" })
        {
            res[key] = line;
        }
        res["MaterialDesign.Brush.ListView.Selected"] = B(0x26B8FF4D);
        res["MaterialDesign.Brush.DataGrid.Selected"] = B(0x26B8FF4D);
        res["MaterialDesign.Brush.ListView.Hover"] = B(0x12B8FF4D);
        res["MaterialDesign.Brush.DataGrid.RowHoverBackground"] = B(0x12B8FF4D);
    }

    private void ModifyFontSize()
    {
        double size = CurrentFontSize;
        if (size < Global.MinFontSize)
        {
            return;
        }

        Application.Current.Resources["StdFontSize"] = size;
        Application.Current.Resources["StdFontSize1"] = size + 1;
        Application.Current.Resources["StdFontSize-1"] = size - 1;
    }

    public void ChangePrimaryColor(System.Windows.Media.Color color)
    {
        var theme = _paletteHelper.GetTheme();

        theme.PrimaryLight = new ColorPair(color.Lighten());
        theme.PrimaryMid = new ColorPair(color);
        theme.PrimaryDark = new ColorPair(color.Darken());

        _paletteHelper.SetTheme(theme);
    }

    public static void RegisterSystemColorSet(Config config, Action updateFunc)
    {
        SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            if ((e.Category == UserPreferenceCategory.Color || e.Category == UserPreferenceCategory.General)
                && config.UiItem.CurrentTheme == nameof(ETheme.FollowSystem))
            {
                updateFunc?.Invoke();
            }
        };
    }
}
