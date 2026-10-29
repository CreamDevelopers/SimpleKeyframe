using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;

namespace KeyframeStudio.Native;

public partial class NativeValueEditor : UserControl
{
    private bool refreshing;
    private bool editing;
    private static readonly MethodInfo BeginBridge = typeof(AnimationSlider).GetMethod("Slider_BeginEdit", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo EndBridge = typeof(AnimationSlider).GetMethod("Slider_EndEdit", BindingFlags.NonPublic | BindingFlags.Instance)!;
    public static readonly DependencyProperty HostProperty = DependencyProperty.Register(nameof(Host), typeof(AnimationSlider),
        typeof(NativeValueEditor), new PropertyMetadata(null, (owner, _) => ((NativeValueEditor)owner).Refresh()));
    public AnimationSlider? Host { get => (AnimationSlider?)GetValue(HostProperty); set => SetValue(HostProperty, value); }
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double),
        typeof(NativeValueEditor), new PropertyMetadata(0d, (owner, args) => ((NativeValueEditor)owner).Commit((double)args.NewValue)));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public NativeValueEditor()
    {
        InitializeComponent();
        var menu = new ContextMenu();
        foreach (var entry in new[]
        {
            ("直線", AnimationType.直線移動), ("イーズイン", AnimationType.Cubic_In),
            ("イーズアウト", AnimationType.Cubic_Out), ("イーズインアウト", AnimationType.Cubic_InOut),
            ("ホールド", AnimationType.瞬間移動)
        })
        {
            var item = new MenuItem { Header = entry.Item1 };
            item.Click += (_, _) =>
            {
                if (Host?.Animation is not { } animation) return;
                BeginNumberEdit(this, EventArgs.Empty);
                try { animation.AnimationType = entry.Item2; }
                catch (Exception error) { NativeRuntime.Report(error); }
                finally { EndNumberEdit(this, EventArgs.Empty); }
            };
            menu.Items.Add(item);
        }
        Diamond.ContextMenu = menu;
        Loaded += (_, _) => { NativeRuntime.Refreshed += Refresh; Refresh(); };
        Unloaded += (_, _) => NativeRuntime.Refreshed -= Refresh;
    }

    public void Refresh()
    {
        if (Host?.Animation is not { } animation || Number is null) return;
        refreshing = true;
        try
        {
            Number.Min = animation.MinValue; Number.Max = animation.MaxValue;
            Number.DefaultMin = Host.DefaultMin; Number.DefaultMax = Host.DefaultMax;
            Number.DefaultValue = Host.DefaultValue; Number.StringFormat = Host.StringFormat;
            Number.Unit = Host.Unit; Number.Delta = Host.Delta;
            if (NativeKeyframes.TryGet(animation, out var item, out var frame, out var fps))
            {
                if (!editing) SetCurrentValue(ValueProperty, animation.GetValue(frame, item.Length, fps));
                var exists = item.KeyFrames.Frames.Contains(frame);
                Diamond.Content = exists ? "◆" : "◇";
                Number.IsEnabled = NativeKeyframes.AutoKey || exists;
                ToolTip = $"現在フレーム {frame} — キーの値を編集します。";
            }
        }
        finally { refreshing = false; }
    }

    private void Commit(double value)
    {
        if (refreshing || Host?.Animation is not { } animation) return;
        var ownTransaction = !editing;
        if (ownTransaction) BeginNumberEdit(this, EventArgs.Empty);
        try { NativeKeyframes.SetValue(animation, value); }
        catch (Exception error) { NativeRuntime.Report(error); }
        finally { if (ownTransaction) EndNumberEdit(this, EventArgs.Empty); }
    }

    private void BeginNumberEdit(object? sender, EventArgs args)
    {
        if (editing || Host is null) return;
        BeginBridge.Invoke(Host, [Host, EventArgs.Empty]);
        editing = true;
    }

    private void EndNumberEdit(object? sender, EventArgs args)
    {
        if (!editing || Host is null) return;
        editing = false;
        EndBridge.Invoke(Host, [Host, EventArgs.Empty]);
        Refresh();
    }

    private void ToggleKey(object sender, RoutedEventArgs args)
    {
        if (Host?.Animation is not { } animation || !NativeKeyframes.TryGet(animation, out var item, out var frame, out var fps)) return;
        BeginNumberEdit(this, EventArgs.Empty);
        try
        {
            if (item.KeyFrames.Frames.Contains(frame)) NativeKeyframes.Remove(item, frame);
            else NativeKeyframes.Add(item, frame, fps);
        }
        catch (Exception error) { NativeRuntime.Report(error); }
        finally { EndNumberEdit(this, EventArgs.Empty); }
    }
}
