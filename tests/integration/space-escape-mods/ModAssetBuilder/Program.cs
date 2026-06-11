using Modulus.ModCompiler;

namespace ModAssetBuilder;

class Program
{
    static int Main(string[] args)
    {
        var modDirectory = args.Length > 0
            ? args[0]
            : Path.Combine(GetProjectRoot(), "tests", "integration", "space-escape-mods", "mod-assets");

        Console.WriteLine($"ModAssetBuilder — Compiling mod assets using ModCompilerService");
        Console.WriteLine($"  Mod directory: {modDirectory}");

        var compiler = new ModCompilerService();
        var result = compiler.CompileMod(modDirectory);

        if (result.Success)
        {
            Console.WriteLine($"Compiled {result.EntryCount} assets, {result.IndexEntries} index entries, {result.DataFileCount} data files");
            return 0;
        }
        else
        {
            Console.Error.WriteLine($"Compilation failed:");
            foreach (var error in result.Errors)
                Console.Error.WriteLine($"  {error}");
            return 1;
        }
    }

    static string GetProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "build", "Stride.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return @"D:\Modulus-Game-Engine";
    }
}
