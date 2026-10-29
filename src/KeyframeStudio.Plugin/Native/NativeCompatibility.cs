using System.Reflection;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.UndoRedo;

namespace KeyframeStudio.Native;

public static class NativeCompatibility
{
    public static void Validate() => Validate(
        typeof(Timeline).Assembly.GetType("YukkuriMovieMaker.ViewModels.MainViewModel"),
        typeof(AnimationSlider), typeof(Animation));

    public static void Validate(Type? mainViewModel, Type sliderType, Type animationType)
    {
        var model = mainViewModel?.GetField("model", BindingFlags.NonPublic | BindingFlags.Instance)?.FieldType;
        if (model?.GetProperty("Timeline")?.PropertyType != typeof(Timeline)
            || model.GetProperty("UndoRedoManager")?.PropertyType != typeof(UndoRedoManager))
            throw new NotSupportedException("YMM4のアイテム編集APIを利用できません。");

        foreach (var name in new[] { "Slider_BeginEdit", "Slider_EndEdit" })
        {
            var method = sliderType.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic,
                null, [typeof(object), typeof(EventArgs)], null);
            if (method?.ReturnType != typeof(void))
                throw new NotSupportedException("YMM4の数値編集APIを利用できません。");
        }
        foreach (var name in new[] { nameof(Animation.AddToEachValues), nameof(Animation.MultiplyToEachValues) })
        {
            if (animationType.GetMethod(name, [typeof(double)])?.ReturnType != typeof(void))
                throw new NotSupportedException("YMM4のプレビュー編集APIを利用できません。");
        }
    }
}
