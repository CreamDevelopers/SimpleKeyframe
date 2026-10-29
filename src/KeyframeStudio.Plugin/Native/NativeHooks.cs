using System.Reflection;
using HarmonyLib;
using YukkuriMovieMaker.Commons;

namespace KeyframeStudio.Native;

public static class NativeHooks
{
    private const string PatchId = "KeyframeStudio.Native.v2";
    private static Harmony? harmony;

    public static void Install()
    {
        if (harmony is not null) return;
        var candidate = new Harmony(PatchId);
        try
        {
            candidate.Patch(AccessTools.Method(typeof(Animation), nameof(Animation.AddToEachValues)),
                prefix: new HarmonyMethod(typeof(NativeHooks), nameof(AddPrefix)));
            candidate.Patch(AccessTools.Method(typeof(Animation), nameof(Animation.MultiplyToEachValues)),
                prefix: new HarmonyMethod(typeof(NativeHooks), nameof(MultiplyPrefix)));
            harmony = candidate;
        }
        catch
        {
            candidate.UnpatchAll(PatchId);
            throw;
        }
    }

    public static void Uninstall()
    {
        harmony?.UnpatchAll(PatchId);
        harmony = null;
    }

    // Unregistered, unselected and non-animated values retain the host's original behavior.
    private static bool AddPrefix(Animation __instance, double delta) => Apply(__instance, delta, false);
    private static bool MultiplyPrefix(Animation __instance, double delta) => Apply(__instance, delta, true);

    private static bool Apply(Animation animation, double delta, bool multiply)
    {
        try { return !NativeKeyframes.ApplyDelta(animation, delta, multiply); }
        catch (Exception error) { NativeRuntime.Report(error); return false; }
    }
}
