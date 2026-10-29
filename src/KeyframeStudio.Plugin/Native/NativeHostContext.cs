using System.Reflection;
using System.Windows;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.UndoRedo;

namespace KeyframeStudio.Native;

/// <summary>The version-specific adapter is confined here; missing members disable integration.</summary>
public sealed record NativeHostContext(Timeline Timeline, UndoRedoManager Undo, object MainViewModel)
{
    private static readonly Type MainViewModelType = typeof(Timeline).Assembly.GetType("YukkuriMovieMaker.ViewModels.MainViewModel", true)!;
    private static readonly FieldInfo ModelField = MainViewModelType.GetField("model", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new MissingFieldException(MainViewModelType.FullName, "model");

    public static NativeHostContext? FromWindow(Window? window)
    {
        while (window is not null)
        {
            if (window.DataContext is { } viewModel && MainViewModelType.IsInstanceOfType(viewModel))
            {
                var model = ModelField.GetValue(viewModel)!;
                var type = model.GetType();
                return new NativeHostContext((Timeline)type.GetProperty("Timeline")!.GetValue(model)!,
                    (UndoRedoManager)type.GetProperty("UndoRedoManager")!.GetValue(model)!, viewModel);
            }
            window = window.Owner;
        }
        return null;
    }

    public IVideoItem[] Targets => Timeline.SelectedItems.OfType<IVideoItem>()
        .Where(item => !item.IsLocked && Timeline.CurrentFrame >= item.Frame && Timeline.CurrentFrame < item.Frame + item.Length).ToArray();

    public void RegisterSelection()
    {
        foreach (var item in Targets)
            NativeKeyframes.Register(item, () => Timeline.CurrentFrame - item.Frame, Timeline.VideoInfo.FPS,
                () => Timeline.SelectedItems.Contains(item));
    }

    public void Add(int? absoluteFrame = null)
    {
        var frame = absoluteFrame ?? Timeline.CurrentFrame;
        Undo.Record();
        foreach (var item in Timeline.SelectedItems.OfType<IVideoItem>())
            NativeKeyframes.Add(item, frame - item.Frame, Timeline.VideoInfo.FPS);
        Undo.Record();
        RegisterSelection();
    }

    public void Remove()
    {
        Undo.Record();
        foreach (var item in Targets) NativeKeyframes.Remove(item, Timeline.CurrentFrame - item.Frame);
        Undo.Record();
    }

    public int? CursorFrame()
    {
        var viewModel = MainViewModel.GetType().GetProperty("ActiveTimelineViewModel")?.GetValue(MainViewModel);
        var reactive = viewModel?.GetType().GetProperty("TimelineCursorPositionWhenRightClick")?.GetValue(viewModel);
        if (reactive?.GetType().GetProperty("Value")?.GetValue(reactive) is not Point point) return null;
        return Math.Max(0, (int)(point.X / SettingsBase<YMMSettings>.Default.TimelineZoom * 100));
    }
}
