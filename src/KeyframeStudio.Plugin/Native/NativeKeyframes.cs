using System.Runtime.CompilerServices;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Project.Items;

namespace KeyframeStudio.Native;

/// <summary>Edits YMM4's own KeyFrames and AnimationValue objects; no parallel project data.</summary>
public static class NativeKeyframes
{
    private sealed record Registration(IVideoItem Item, Func<int> Frame, Func<bool> IsSelected, int Fps);
    private static readonly ConditionalWeakTable<Animation, Registration> registrations = new();
    public static bool Enabled { get; set; } = true;
    public static bool AutoKey { get; set; } = true;

    public static Animation[] Channels(IVideoItem item) => [item.X, item.Y, item.Z, item.Zoom, item.Rotation, item.Opacity];

    public static void Register(IVideoItem item, Func<int> relativeFrame, int fps, Func<bool>? selected = null)
    {
        foreach (var animation in Channels(item))
        {
            registrations.Remove(animation);
            registrations.Add(animation, new Registration(item, relativeFrame, selected ?? (() => true), fps));
        }
    }

    public static bool TryGet(Animation animation, out IVideoItem item, out int frame, out int fps)
    {
        item = null!; frame = 0; fps = 0;
        if (!Enabled || !registrations.TryGetValue(animation, out var registration) || !registration.IsSelected()) return false;
        item = registration.Item; frame = registration.Frame(); fps = registration.Fps;
        return !item.IsLocked && frame >= 0 && frame < item.Length && item.KeyFrames.Count > 0;
    }

    public static bool Supports(Animation animation) => animation.AnimationType.IsKeyFrameSupported();

    public static bool Add(IVideoItem item, int frame, int fps)
    {
        if (item.IsLocked || frame < 0 || frame >= item.Length || fps <= 0) return false;
        foreach (var animation in Channels(item))
        {
            animation.SetKeyFrames(item.KeyFrames);
            animation.SetAnimationParameters(item.Length, fps);
            if (animation.AnimationType != AnimationType.なし) continue;
            animation.BeginEdit();
            animation.AnimationType = AnimationType.直線移動;
            animation.EndEditAsync().GetAwaiter().GetResult();
            // Keep an enclosing host transaction from normalizing the new values a second time.
            animation.BeginEdit();
        }
        if (item.KeyFrames.Frames.Contains(frame)) return false;
        item.KeyFrames.Insert(frame);
        return true;
    }

    public static bool Remove(IVideoItem item, int frame)
    {
        if (item.IsLocked || !item.KeyFrames.Frames.Contains(frame)) return false;
        item.KeyFrames.Remove(frame);
        return true;
    }

    public static bool SetValue(Animation animation, double value)
    {
        if (!double.IsFinite(value) || !TryGet(animation, out var item, out var frame, out var fps) || !Supports(animation)) return false;
        if (!item.KeyFrames.Frames.Contains(frame))
        {
            if (!AutoKey) return false;
            Add(item, frame, fps);
        }
        var index = item.KeyFrames.Frames.IndexOf(frame) + 1;
        if (index < 1 || index >= animation.Values.Count) throw new InvalidOperationException("YMM4のキーフレーム数と値の数が一致しません。");
        value = Math.Clamp(value, animation.MinValue, animation.MaxValue);
        animation.Values[index].Value = value;
        // Native endpoints are implicit. Hold outside the first/last explicit marker.
        if (index == 1) animation.Values[0].Value = value;
        if (index == item.KeyFrames.Count) animation.Values[^1].Value = value;
        return true;
    }

    public static bool ApplyDelta(Animation animation, double delta, bool multiply)
    {
        if (!TryGet(animation, out var item, out var frame, out var fps) || !Supports(animation)) return false;
        if (!double.IsFinite(delta)) throw new ArgumentOutOfRangeException(nameof(delta));
        if (!AutoKey && !item.KeyFrames.Frames.Contains(frame)) return true;
        var current = animation.GetValue(frame, item.Length, fps);
        SetValue(animation, multiply ? current * delta : current + delta);
        return true;
    }
}
