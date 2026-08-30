// Modulus.Mod.PackTool — CLI for mod verification, GUID generation, and packaging.
//
// Usage:
//   dotnet Modulus.Mod.PackTool.dll gen-guids --db-path <path> --output <path>
//   dotnet Modulus.Mod.PackTool.dll verify --mod-dir <path> [--game-db-path <path>] [--strict]
//   dotnet Modulus.Mod.PackTool.dll pack --source <path> --output <path>

using Modulus.Mod.PackTool.GuidGeneration;
using Modulus.Mod.PackTool.Packaging;
using Modulus.Mod.PackTool.Verification;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0].ToLowerInvariant();
var remainingArgs = args.Skip(1).ToArray();

return command switch
{
    "gen-guids" => RunGenGuids(remainingArgs),
    "verify" => RunVerify(remainingArgs),
    "pack" => RunPack(remainingArgs),
    "help" or "--help" or "-h" or "/?" => PrintUsage(),
    _ => UnknownCommand(command),
};

static int RunGenGuids(string[] args)
{
    var dbPath = GetOptionValue(args, "--db-path");
    var outputPath = GetOptionValue(args, "--output");

    if (string.IsNullOrEmpty(dbPath) || string.IsNullOrEmpty(outputPath))
    {
        Console.Error.WriteLine("[ModPackTool] gen-guids requires --db-path and --output arguments.");
        return 1;
    }

    var count = GuidGenerator.Generate(dbPath, outputPath);
    return count > 0 ? 0 : 1;
}

static int RunVerify(string[] args)
{
    var modDir = GetOptionValue(args, "--mod-dir");
    var gameDbPath = GetOptionValue(args, "--game-db-path");
    var strict = HasFlag(args, "--strict");

    if (string.IsNullOrEmpty(modDir))
    {
        Console.Error.WriteLine("[ModPackTool] verify requires --mod-dir argument.");
        return 1;
    }

    try
    {
        var result = ModVerifier.Verify(modDir,
            string.IsNullOrEmpty(gameDbPath) ? null : gameDbPath,
            strict);

        result.Print();
        return result.ExitCode;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[ModPackTool] Verify failed: {ex.Message}");
        return 1;
    }
}

static int RunPack(string[] args)
{
    var sourceDir = GetOptionValue(args, "--source");
    var outputPath = GetOptionValue(args, "--output");

    if (string.IsNullOrEmpty(sourceDir) || string.IsNullOrEmpty(outputPath))
    {
        Console.Error.WriteLine("[ModPackTool] pack requires --source and --output arguments.");
        return 1;
    }

    try
    {
        ModPacker.Pack(sourceDir, outputPath);
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[ModPackTool] Pack failed: {ex.Message}");
        return 1;
    }
}

static int PrintUsage()
{
    Console.WriteLine("""
        Modulus.Mod.PackTool — Mod compilation pipeline CLI

        Usage:
          Modulus.Mod.PackTool gen-guids --db-path <path> --output <path>
            Reads the Stride ObjectDatabase index file and generates asset-guids.json
            for runtime ContentIndexMap injection.

          Modulus.Mod.PackTool verify --mod-dir <path> [--game-db-path <path>] [--strict]
            Validates mod structure, manifest, assemblies, dependencies, and checks
            for asset URL/GUID collisions with the game database.
            Exit codes: 0=valid, 1=errors, 2=warnings only.

          Modulus.Mod.PackTool pack --source <path> --output <path>
            Creates a .modpkg ZIP archive from a staged mod directory.
        """);
    return 0;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"[ModPackTool] Unknown command: '{command}'");
    Console.Error.WriteLine("Run 'Modulus.Mod.PackTool help' for usage.");
    return 1;
}

static string? GetOptionValue(string[] args, string optionName)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i].Equals(optionName, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }
    return null;
}

static bool HasFlag(string[] args, string flagName)
{
    return args.Any(a => a.Equals(flagName, StringComparison.OrdinalIgnoreCase));
}
