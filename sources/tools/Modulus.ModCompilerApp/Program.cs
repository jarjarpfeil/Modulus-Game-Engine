using Modulus.ModCompiler;

namespace Modulus.ModCompilerApp;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        var modDirectory = args[0];
        string? outputDirectory = null;

        for (int i = 1; i < args.Length; i++)
        {
            if ((args[i] == "--output" || args[i] == "-o") && i + 1 < args.Length)
            {
                outputDirectory = args[++i];
            }
        }

        Console.WriteLine($"ModCompiler — Compiling mod assets");
        Console.WriteLine($"  Mod directory: {modDirectory}");
        if (outputDirectory != null)
            Console.WriteLine($"  Output: {outputDirectory}");
        Console.WriteLine();

        var compiler = new ModCompilerService();
        var result = compiler.CompileMod(modDirectory, outputDirectory);

        Console.WriteLine();
        if (result.Success)
        {
            Console.WriteLine($"Compilation successful:");
            Console.WriteLine($"  Assets compiled: {result.EntryCount}");
            Console.WriteLine($"  Index entries:   {result.IndexEntries}");
            Console.WriteLine($"  Data files:      {result.DataFileCount}");
            Console.WriteLine($"  Output:          {result.OutputDirectory}");
            return 0;
        }
        else
        {
            Console.Error.WriteLine($"Compilation failed with {result.Errors.Count} error(s):");
            foreach (var error in result.Errors)
                Console.Error.WriteLine($"  ERROR: {error}");
            return 1;
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("ModCompiler — Compile mod assets into ObjectDatabase format");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  modcompiler <mod-directory> [--output <output-directory>]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  <mod-directory>     Path to the mod directory containing mod.json");
        Console.WriteLine("  --output, -o        Output directory for compiled assets (default: mod/assets/)");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  modcompiler ./mods/my-mod");
        Console.WriteLine("  modcompiler ./mods/my-mod --output ./mods/my-mod/compiled-assets");
    }
}
