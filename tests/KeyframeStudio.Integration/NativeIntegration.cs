using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KeyframeStudio.Native;
using Newtonsoft.Json;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.UndoRedo;

internal static class NativeIntegration
{
    public static void Run(Action<string, Action> test)
    {
        test("Required host APIs are available", NativeCompatibility.Validate);
        test("Unavailable host APIs are rejected", () =>
        {
            try { NativeCompatibility.Validate(typeof(object), typeof(AnimationSlider), typeof(Animation)); }
            catch (NotSupportedException) { return; }
            throw new Exception("Expected unsupported API detection");
        });
        test("Plugin contains no legacy video effect or data model", () =>
        {
            var types = typeof(NativePlugin).Assembly.GetTypes();
            Require(types.All(type => !typeof(IVideoEffect).IsAssignableFrom(type)));
            Require(types.All(type => type.Namespace != "KeyframeStudio.Core"));
        });
        test("Native markers and ordinary property values", () =>
        {
            var fixture = new Fixture();
            Require(NativeKeyframes.Add(fixture.Item, 0, 60));
            Require(NativeKeyframes.SetValue(fixture.Item.X, 10));
            fixture.Frame = 60;
            Require(NativeKeyframes.Add(fixture.Item, 60, 60));
            NativeKeyframes.SetValue(fixture.Item.X, 110);
            Require(fixture.Item.KeyFrames.Frames.SequenceEqual(new[] { 0, 60 }));
            Equal(60, fixture.Item.X.GetValue(30, 120, 60));
            Equal(10, fixture.Item.X.GetValue(0, 120, 60));
            Equal(110, fixture.Item.X.GetValue(119, 120, 60));
        });
        test("Native duplicate and invalid keys", () =>
        {
            var fixture = new Fixture();
            Require(!NativeKeyframes.Add(fixture.Item, -1, 60));
            Require(!NativeKeyframes.Add(fixture.Item, 120, 60));
            Require(NativeKeyframes.Add(fixture.Item, 0, 60));
            Require(!NativeKeyframes.Add(fixture.Item, 0, 60));
            Require(fixture.Item.KeyFrames.Count == 1);
            fixture.Item.IsLocked = true;
            Require(!NativeKeyframes.Add(fixture.Item, 60, 60));
            Require(!NativeKeyframes.Remove(fixture.Item, 0));
        });
        test("Native first and last keys hold endpoints", () =>
        {
            var fixture = new Fixture { Frame = 30 };
            NativeKeyframes.Add(fixture.Item, 30, 60);
            NativeKeyframes.SetValue(fixture.Item.Rotation, 45);
            Equal(45, fixture.Item.Rotation.GetValue(0, 120, 60));
            Equal(45, fixture.Item.Rotation.GetValue(119, 120, 60));
        });
        test("Native automatic keys snapshot other channels", () =>
        {
            var fixture = new Fixture();
            NativeKeyframes.Add(fixture.Item, 0, 60);
            NativeKeyframes.SetValue(fixture.Item.X, 10);
            fixture.Frame = 60;
            NativeKeyframes.SetValue(fixture.Item.X, 110);
            fixture.Frame = 30;
            NativeKeyframes.SetValue(fixture.Item.Rotation, 90);
            Require(fixture.Item.KeyFrames.Frames.SequenceEqual(new[] { 0, 30, 60 }));
            Equal(60, fixture.Item.X.GetValue(30, 120, 60));
            Equal(90, fixture.Item.Rotation.GetValue(30, 120, 60));
        });
        test("Native disabled auto-key leaves interpolation unchanged", () =>
        {
            var fixture = Pair();
            fixture.Frame = 30;
            NativeKeyframes.AutoKey = false;
            Require(!NativeKeyframes.SetValue(fixture.Item.X, 999));
            Require(fixture.Item.KeyFrames.Count == 2);
            Equal(60, fixture.Item.X.GetValue(30, 120, 60));
            NativeKeyframes.AutoKey = true;
        });
        test("Native keys and values survive host serialization", () =>
        {
            var fixture = Pair();
            var json = JsonConvert.SerializeObject(fixture.Item);
            var restored = JsonConvert.DeserializeObject<ModelItem>(json)!;
            restored.SetFPS(60);
            Require(restored.KeyFrames.Frames.SequenceEqual(new[] { 0, 60 }));
            Equal(60, restored.X.GetValue(30, restored.Length, 60));
            Require(restored.VideoEffects.Count == 0);
        });
        test("Native key creation is undoable as one operation", () =>
        {
            var fixture = new Fixture();
            var undo = new UndoRedoManager();
            undo.Subscribe(fixture.Item);
            NativeKeyframes.Add(fixture.Item, 0, 60);
            NativeKeyframes.SetValue(fixture.Item.X, 10);
            undo.Record();
            fixture.Frame = 60;
            NativeKeyframes.Add(fixture.Item, 60, 60);
            NativeKeyframes.SetValue(fixture.Item.X, 110);
            undo.Record();
            undo.UndoAsync().GetAwaiter().GetResult();
            Require(fixture.Item.KeyFrames.Frames.SequenceEqual(new[] { 0 }));
            Equal(10, fixture.Item.X.GetValue(60, 120, 60));
            undo.RedoAsync().GetAwaiter().GetResult();
            Require(fixture.Item.KeyFrames.Frames.SequenceEqual(new[] { 0, 60 }));
            Equal(110, fixture.Item.X.GetValue(60, 120, 60));
        });
        test("Native single row numeric control updates current key", () =>
        {
            var fixture = Pair();
            fixture.Frame = 60;
            var slider = new AnimationSlider { Animation = fixture.Item.X, Animations = [fixture.Item.X], StringFormat = "F1", Unit = "px", Delta = 0.1 };
            var begins = 0; var ends = 0;
            slider.BeginEdit += (_, _) => begins++;
            slider.EndEdit += (_, _) => ends++;
            var editor = new NativeValueEditor { Host = slider };
            editor.Value = 210;
            Equal(210, fixture.Item.X.GetValue(60, 120, 60));
            Equal(10, fixture.Item.X.GetValue(0, 120, 60));
            Require(begins == 1 && ends == 1);
        });
        foreach (var width in new[] { 100d, 160d, 240d })
        {
            test($"Native numeric row fits {width}px", () =>
            {
                var fixture = Pair();
                var slider = new AnimationSlider { Animation = fixture.Item.X, Animations = [fixture.Item.X], StringFormat = "F1", Unit = "px" };
                NativeRuntime.ApplySlider(slider);
                slider.Measure(new Size(width, 26));
                slider.Arrange(new Rect(0, 0, width, 26));
                slider.UpdateLayout();
                Require(slider.ActualWidth <= width + 0.5);
                Require(slider.Template.VisualTree.Type == typeof(NativeValueEditor));
                NativeKeyframes.Enabled = false;
                NativeRuntime.ApplySlider(slider);
                Require(slider.ReadLocalValue(Control.TemplateProperty) == DependencyProperty.UnsetValue);
                NativeKeyframes.Enabled = true;
            });
        }
        test("Native preview delta patch changes current key only", () =>
        {
            var fixture = Pair();
            fixture.Frame = 60;
            NativeHooks.Install();
            try
            {
                typeof(Animation).GetMethod(nameof(Animation.AddToEachValues))!.Invoke(fixture.Item.X, [5d]);
                Equal(115, fixture.Item.X.GetValue(60, 120, 60));
                Equal(10, fixture.Item.X.GetValue(0, 120, 60));
                var untouched = new Animation(2);
                typeof(Animation).GetMethod(nameof(Animation.AddToEachValues))!.Invoke(untouched, [3d]);
                Equal(5, untouched.GetValue(0, 120, 60));
            }
            finally { NativeHooks.Uninstall(); }
        });
        test("Native preview scale patch changes current key only", () =>
        {
            var fixture = Pair();
            fixture.Frame = 60;
            NativeHooks.Install();
            try
            {
                typeof(Animation).GetMethod(nameof(Animation.MultiplyToEachValues))!.Invoke(fixture.Item.Zoom, [2d]);
                Equal(200, fixture.Item.Zoom.GetValue(60, 120, 60));
                Equal(100, fixture.Item.Zoom.GetValue(0, 120, 60));
            }
            finally { NativeHooks.Uninstall(); }
        });
        test("Native shortcuts ignore text fields but accept host focus sinks", () =>
        {
            Require(NativeRuntime.IsTextInput(new TextBox()));
            Require(NativeRuntime.IsTextInput(new PasswordBox()));
            Require(!NativeRuntime.IsTextInput(new TextBox { Name = "focusSink" }));
            Require(!NativeRuntime.IsTextInput(new TextBox { Name = "FocusSink" }));
            Require(!NativeRuntime.IsTextInput(new Button()));
        });
        test("Native host adapter matches target SDK", () =>
        {
            var assembly = typeof(Timeline).Assembly;
            var mainViewModel = assembly.GetType("YukkuriMovieMaker.ViewModels.MainViewModel", true)!;
            var field = mainViewModel.GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Require(field.FieldType.GetProperty("Timeline")!.PropertyType == typeof(Timeline));
            Require(field.FieldType.GetProperty("UndoRedoManager")!.PropertyType == typeof(UndoRedoManager));
            Require(typeof(AnimationSlider).GetMethod("Slider_BeginEdit", BindingFlags.Instance | BindingFlags.NonPublic) is not null);
            Require(typeof(AnimationSlider).GetMethod("Slider_EndEdit", BindingFlags.Instance | BindingFlags.NonPublic) is not null);
        });
        test("Native preview edit adds a key between existing keys", () =>
        {
            var fixture = Pair(); fixture.Frame = 30;
            NativeHooks.Install();
            try
            {
                typeof(Animation).GetMethod(nameof(Animation.AddToEachValues))!.Invoke(fixture.Item.X, [5d]);
                Require(fixture.Item.KeyFrames.Frames.SequenceEqual(new[] { 0, 30, 60 }));
                Equal(65, fixture.Item.X.GetValue(30, 120, 60));
                Equal(10, fixture.Item.X.GetValue(0, 120, 60));
                Equal(110, fixture.Item.X.GetValue(60, 120, 60));
            }
            finally { NativeHooks.Uninstall(); }
        });
        test("Native hooks preserve disabled and unselected items", () =>
        {
            var fixture = Pair(); fixture.Frame = 60;
            NativeKeyframes.Register(fixture.Item, () => fixture.Frame, 60, () => false);
            NativeHooks.Install();
            try
            {
                typeof(Animation).GetMethod(nameof(Animation.AddToEachValues))!.Invoke(fixture.Item.X, [5d]);
                Equal(15, fixture.Item.X.GetValue(0, 120, 60));
                Equal(115, fixture.Item.X.GetValue(60, 120, 60));
                NativeKeyframes.Register(fixture.Item, () => fixture.Frame, 60);
                NativeKeyframes.Enabled = false;
                typeof(Animation).GetMethod(nameof(Animation.AddToEachValues))!.Invoke(fixture.Item.X, [5d]);
                Equal(20, fixture.Item.X.GetValue(0, 120, 60));
                Equal(120, fixture.Item.X.GetValue(60, 120, 60));
            }
            finally { NativeKeyframes.Enabled = true; NativeHooks.Uninstall(); }
        });
        test("Native item split preserves animation on both sides", () =>
        {
            var fixture = Pair();
            var (left, right) = fixture.Item.Split(45);
            var leftVideo = (IVideoItem)left;
            var rightVideo = (IVideoItem)right;
            Equal(60, leftVideo.X.GetValue(30, leftVideo.Length, 60));
            Equal(85, rightVideo.X.GetValue(0, rightVideo.Length, 60));
            Equal(110, rightVideo.X.GetValue(15, rightVideo.Length, 60));
            Require(left.Length == 45 && right.Length == 75);
            Require(right.Frame == fixture.Item.Frame + 45);
        });
    }

    private sealed class Fixture
    {
        public ModelItem Item { get; } = new() { Frame = 100, Length = 120 };
        public int Frame { get; set; }
        public Fixture()
        {
            NativeKeyframes.Enabled = true; NativeKeyframes.AutoKey = true;
            Item.SetFPS(60);
            NativeKeyframes.Register(Item, () => Frame, 60);
        }
    }
    private static Fixture Pair()
    {
        var fixture = new Fixture();
        NativeKeyframes.Add(fixture.Item, 0, 60); NativeKeyframes.SetValue(fixture.Item.X, 10);
        fixture.Frame = 60;
        NativeKeyframes.Add(fixture.Item, 60, 60); NativeKeyframes.SetValue(fixture.Item.X, 110);
        return fixture;
    }
    private static void Require(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    private static void Equal(double expected, double actual) { if (Math.Abs(expected - actual) > 1e-6) throw new Exception($"Expected {expected}, actual {actual}"); }

    // Use the real VisualItem model without starting YMM4's application/default-plugin loader.
    // Rendering and EXO are deliberately outside these model-level tests.
    public sealed class ModelItem : VisualItem
    {
        public override Color ItemColor { get; set; }
        public override TimeSpan OriginalContentLength => TimeSpan.Zero;
        public override TimeSpan ContentLength => TimeSpan.Zero;
        public override ISource CreateVideoSource(IGraphicsDevices devices, Scene scene) => throw new NotSupportedException("Model-only fixture");
        public override IAsyncEnumerable<ExoItem> GetExoItemsAsync(ExoOutputDescription outputDescription) => throw new NotSupportedException("Model-only fixture");
    }
}
