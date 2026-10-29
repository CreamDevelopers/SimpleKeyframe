using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Project;

namespace KeyframeStudio.Native;

public static class NativeRuntime
{
    private sealed record SliderState(object OriginalTemplate);
    private static readonly ConditionalWeakTable<AnimationSlider, SliderState> sliderStates = new();
    private static DispatcherTimer? timer;
    private static bool scheduled;
    private static bool updating;
    public static string Status { get; private set; } = "初期化待ち";
    public static event Action? Refreshed;
    public static bool Supported { get; private set; }

    public static void Schedule()
    {
        if (scheduled || Application.Current is null) return;
        scheduled = true;
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Start));
    }

    private static void Start()
    {
        try
        {
            NativeCompatibility.Validate();
            NativeHooks.Install();
            NativeKeyframes.Enabled = NativeSettings.Default.Enabled;
            NativeKeyframes.AutoKey = NativeSettings.Default.AutoKey;
            Supported = true;
            Status = "K: キー追加 / Shift+K: 削除。キー位置で通常の数値欄・プレビューを編集できます。";
            EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(MenuOpened));
            EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyDownEvent, new KeyEventHandler(KeyDown));
            timer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) => Refresh(), Application.Current.Dispatcher);
            Application.Current.Exit += (_, _) => { timer.Stop(); NativeKeyframes.Enabled = false; NativeHooks.Uninstall(); };
        }
        catch (Exception error) { NativeKeyframes.Enabled = false; Report(error); }
    }

    public static void Report(Exception error)
    {
        Status = NativePlugin.DisplayName + ": " + error.GetBaseException().Message;
        Trace.TraceError("KeyframeStudio.Native: {0}", error);
    }

    private static void Refresh()
    {
        if (updating) return;
        updating = true;
        try
        {
            foreach (Window window in Application.Current.Windows)
            {
                var context = NativeHostContext.FromWindow(window);
                if (context is null) continue;
                context.RegisterSelection();
                foreach (var slider in Descendants<AnimationSlider>(window)) ApplySlider(slider);
            }
            Refreshed?.Invoke();
        }
        catch (Exception error) { Report(error); }
        finally { updating = false; }
    }

    public static void ApplySlider(AnimationSlider slider)
    {
        if (slider.Animation is { } animation && NativeKeyframes.Supports(animation) && NativeKeyframes.TryGet(animation, out _, out _, out _))
        {
            if (sliderStates.TryGetValue(slider, out _)) return;
            sliderStates.Add(slider, new SliderState(slider.ReadLocalValue(Control.TemplateProperty)));
            var factory = new FrameworkElementFactory(typeof(NativeValueEditor));
            factory.SetBinding(NativeValueEditor.HostProperty, new Binding { RelativeSource = RelativeSource.TemplatedParent });
            slider.Template = new ControlTemplate(typeof(AnimationSlider)) { VisualTree = factory };
        }
        else if (sliderStates.TryGetValue(slider, out var original))
        {
            sliderStates.Remove(slider);
            if (original.OriginalTemplate == DependencyProperty.UnsetValue) slider.ClearValue(Control.TemplateProperty);
            else slider.SetValue(Control.TemplateProperty, original.OriginalTemplate);
        }
    }

    private static void KeyDown(object sender, KeyEventArgs args)
    {
        if (!Supported || !NativeKeyframes.Enabled || args.Handled || args.IsRepeat || args.Key != Key.K
            || Keyboard.Modifiers is not (ModifierKeys.None or ModifierKeys.Shift)) return;
        if (Keyboard.FocusedElement is DependencyObject focus && IsTextInput(focus)) return;
        if (sender is not Window window || NativeHostContext.FromWindow(window) is not { } context || context.Targets.Length == 0) return;
        try
        {
            if (Keyboard.Modifiers == ModifierKeys.Shift) context.Remove(); else context.Add();
            args.Handled = true;
            Refresh();
        }
        catch (Exception error) { Report(error); }
    }

    public static bool IsTextInput(DependencyObject element)
    {
        for (DependencyObject? node = element; node is not null; node = Parent(node))
        {
            // YMM4 uses an invisible TextBox named FocusSink to receive timeline shortcuts.
            if (node is TextBoxBase && node is FrameworkElement input && string.Equals(input.Name, "focusSink", StringComparison.OrdinalIgnoreCase)) continue;
            if (node is TextBoxBase or PasswordBox || node.GetType().FullName?.Contains("AvalonEdit", StringComparison.Ordinal) == true) return true;
        }
        return false;
    }

    private static void MenuOpened(object sender, RoutedEventArgs args)
    {
        try { PopulateMenu(sender); }
        catch (Exception error) { Report(error); }
    }

    private static void PopulateMenu(object sender)
    {
        if (!Supported || !NativeKeyframes.Enabled || sender is not ContextMenu menu || menu.PlacementTarget is not { } target) return;
        var isTimeline = false;
        for (DependencyObject? node = target; node is not null; node = Parent(node))
        {
            var name = (node as FrameworkElement)?.DataContext?.GetType().FullName;
            if (name is "YukkuriMovieMaker.ViewModels.TimelineItemViewModel" or "YukkuriMovieMaker.ViewModels.TimelineViewModel") { isTimeline = true; break; }
        }
        if (!isTimeline || NativeHostContext.FromWindow(Window.GetWindow(target)) is not { } context) return;
        foreach (var old in menu.Items.OfType<FrameworkElement>().Where(item => Equals(item.Tag, "KeyframeStudio.Native")).ToArray()) menu.Items.Remove(old);
        var add = new MenuItem { Header = "キーフレームを追加（再生位置）", InputGestureText = "K", Tag = "KeyframeStudio.Native", IsEnabled = context.Targets.Length > 0 };
        var cursor = new MenuItem { Header = "キーフレームを追加（クリック位置）", Tag = "KeyframeStudio.Native", IsEnabled = context.CursorFrame() is not null };
        var remove = new MenuItem
        {
            Header = "キーフレームを削除（再生位置）",
            InputGestureText = "Shift+K",
            Tag = "KeyframeStudio.Native",
            IsEnabled = context.Targets.Any(item => item.KeyFrames.Frames.Contains(context.Timeline.CurrentFrame - item.Frame))
        };
        add.Click += (_, _) => Execute(() => context.Add());
        cursor.Click += (_, _) => Execute(() => context.Add(context.CursorFrame()));
        remove.Click += (_, _) => Execute(context.Remove);
        menu.Items.Add(new Separator { Tag = "KeyframeStudio.Native" });
        menu.Items.Add(add); menu.Items.Add(cursor); menu.Items.Add(remove);
    }

    private static void Execute(Action action)
    {
        try { action(); Refresh(); } catch (Exception error) { Report(error); }
    }

    private static DependencyObject? Parent(DependencyObject element) => element is Visual or System.Windows.Media.Media3D.Visual3D
        ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
