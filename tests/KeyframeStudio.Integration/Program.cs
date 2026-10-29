using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var sdk = args.FirstOrDefault() ?? Environment.GetEnvironmentVariable("YMM4DirPath");
        if (string.IsNullOrWhiteSpace(sdk) || !File.Exists(Path.Combine(sdk, "YukkuriMovieMaker.Plugin.dll")))
        {
            Console.Error.WriteLine("YMM4のフォルダーを引数またはYMM4DirPath環境変数で指定してください。");
            return 2;
        }
        AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(sdk, name.Name + ".dll"))
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(sdk, name.Name + ".dll")) : null;
        return Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run()
    {
        _ = new Application();
        var failures = 0;
        var total = 0;
        NativeIntegration.Run((name, test) =>
        {
            total++;
            try { test(); Console.WriteLine("PASS " + name); }
            catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + error); }
        });
        Console.WriteLine($"{total - failures}/{total} passed");
        return failures == 0 ? 0 : 1;
    }
}
