using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;

namespace KeyframeStudio.Native;

public sealed class NativePlugin : IToolPlugin
{
    public const string DisplayName = "シンプルキーフレーム";
    public string Name => DisplayName;
    public Type ViewModelType => typeof(NativeToolViewModel);
    public Type ViewType => typeof(NativeToolView);
    public NativePlugin() => NativeRuntime.Schedule();
}

public sealed class NativeToolViewModel : Bindable, IToolViewModel
{
    public string Title => NativePlugin.DisplayName;
    public bool CanSuspend => false;
    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested { add { } remove { } }
    // Preferences are application settings, so this tool has no per-project state.
    public ToolState SaveState() => new();
    public void LoadState(ToolState stateData) { }
}

public sealed class NativeSettings : SettingsBase<NativeSettings>
{
    private bool enabled = true;
    private bool autoKey = true;
    public override SettingsCategory Category => SettingsCategory.Tool;
    public override string Name => NativePlugin.DisplayName;
    public override bool HasSettingView => false;
    public override object? SettingView => null;
    public bool Enabled
    {
        get => enabled;
        set { if (Set(ref enabled, value)) NativeKeyframes.Enabled = value && NativeRuntime.Supported; }
    }
    public bool AutoKey
    {
        get => autoKey;
        set { if (Set(ref autoKey, value)) NativeKeyframes.AutoKey = value; }
    }
    public override void Initialize() { }
}

public sealed class NativeToolView : UserControl
{
    public NativeToolView()
    {
        var panel = new StackPanel { Margin = new Thickness(12) };
        var title = new TextBlock { Text = NativePlugin.DisplayName, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(title);
        foreach (var option in new[] { (nameof(NativeSettings.Enabled), "有効にする"), (nameof(NativeSettings.AutoKey), "キー間で編集したときに自動でキーを追加") })
        {
            var check = new CheckBox { Content = option.Item2, Margin = new Thickness(0, 4, 0, 4) };
            check.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(option.Item1) { Source = NativeSettings.Default, Mode = BindingMode.TwoWay });
            check.Click += (_, _) => NativeSettings.Default.Save();
            panel.Children.Add(check);
        }
        panel.Children.Add(new TextBlock
        {
            Text = "アイテムを選択して K でキーを追加、Shift+K で削除します。\nキーの位置へ移動して、通常のX・Y・回転やプレビューを編集してください。\n◆が現在位置のキーです。補間は数値欄の◆を右クリックして選べます。\nこのツールを開いたままにする必要はありません。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 10)
        });
        var status = new TextBlock { Text = NativeRuntime.Status, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 };
        Action refresh = () => status.Text = NativeRuntime.Status;
        Loaded += (_, _) => { NativeRuntime.Refreshed += refresh; refresh(); };
        Unloaded += (_, _) => NativeRuntime.Refreshed -= refresh;
        panel.Children.Add(status);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
}
