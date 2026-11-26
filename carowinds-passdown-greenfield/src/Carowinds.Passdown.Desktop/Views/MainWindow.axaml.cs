using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Carowinds.Passdown.Application.Windowing;
using Carowinds.Passdown.Desktop.ViewModels;

namespace Carowinds.Passdown.Desktop.Views;

public partial class MainWindow : Window
{
    public const string TitleBarColorResourceKey = "PassdownTitleBarColor";
    public const string TitleBarTextColorResourceKey = "PassdownTitleBarTextColor";

    private IWindowCaptionStyler? _captionStyler;

    public MainWindow()
    {
        InitializeComponent();

        // Phase 35A: presentation-only forwarding. Activation re-reads Windows reduced motion (changing that setting
        // means leaving the app); the client width lets the sidebar show icons only in narrow windows.
        Activated += (_, _) => (DataContext as MainViewModel)?.OnWindowActivated();

        // Phase 35D: the navy title bar. Applied once the native window exists, and again after a theme change —
        // Avalonia re-sets the frame's dark-mode flag on a theme change, after this event, hence the post.
        Opened += (_, _) => ApplyCaptionStyle();
        ActualThemeVariantChanged += (_, _) => Dispatcher.UIThread.Post(ApplyCaptionStyle, DispatcherPriority.Background);
    }

    /// <summary>
    /// Phase 35D: colours the native title bar (set by the composition root; null in tests and design time, which
    /// keep the platform's title bar). Only the caption's colours change — the window keeps the operating system's
    /// own title bar, so every native window behaviour is untouched.
    /// </summary>
    public IWindowCaptionStyler? CaptionStyler
    {
        get => _captionStyler;
        set
        {
            _captionStyler = value;
            if (IsVisible)
            {
                ApplyCaptionStyle();
            }
        }
    }

    /// <summary>What the last caption-colour request did (diagnostics and tests); null until one was made.</summary>
    public CaptionStyleResult? LastCaptionStyleResult { get; private set; }

    /// <summary>Raised after each caption-colour request with its result (the composition root logs it).</summary>
    public event EventHandler<CaptionStyleResult>? CaptionStyleApplied;

    /// <summary>The title-bar colours from the design tokens (PassdownTokens.axaml), never hard-coded here.</summary>
    public (CaptionColor Caption, CaptionColor Text) ResolveTitleBarColors()
    {
        static CaptionColor ToCaption(Color color) => new(color.R, color.G, color.B);

        var caption = this.TryFindResource(TitleBarColorResourceKey, out var captionValue) && captionValue is Color c ? c : Color.Parse("#1B3553");
        var text = this.TryFindResource(TitleBarTextColorResourceKey, out var textValue) && textValue is Color t ? t : Colors.White;
        return (ToCaption(caption), ToCaption(text));
    }

    private void ApplyCaptionStyle()
    {
        if (_captionStyler is null)
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? 0;
        var (caption, text) = ResolveTitleBarColors();
        var result = _captionStyler.Apply(handle, caption, text);
        LastCaptionStyleResult = result;
        CaptionStyleApplied?.Invoke(this, result);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ClientSizeProperty || change.Property == DataContextProperty)
        {
            (DataContext as MainViewModel)?.UpdateShellWidth(ClientSize.Width);
        }
    }
}
