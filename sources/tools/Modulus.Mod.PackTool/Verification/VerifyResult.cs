// Verification result types: errors and warnings with structured data.

namespace Modulus.Mod.PackTool.Verification;

public enum VerifySeverity
{
    Error,
    Warning,
    Info,
}

public sealed record VerifyMessage(VerifySeverity Severity, string Code, string Message)
{
    public override string ToString() => $"[{Severity}] {Code}: {Message}";
}

public sealed class VerifyResult
{
    public List<VerifyMessage> Messages { get; } = [];

    public bool HasErrors => Messages.Any(m => m.Severity == VerifySeverity.Error);
    public bool HasWarningsOnly => !HasErrors && Messages.Any(m => m.Severity == VerifySeverity.Warning);

    /// <summary>
    /// Exit code: 0=valid, 1=errors, 2=warnings only.
    /// </summary>
    public int ExitCode => HasErrors ? 1 : (HasWarningsOnly ? 2 : 0);

    public void Error(string code, string message) => Messages.Add(new VerifyMessage(VerifySeverity.Error, code, message));
    public void Warning(string code, string message) => Messages.Add(new VerifyMessage(VerifySeverity.Warning, code, message));
    public void Info(string code, string message) => Messages.Add(new VerifyMessage(VerifySeverity.Info, code, message));

    public void Print()
    {
        foreach (var msg in Messages)
        {
            var prefix = msg.Severity switch
            {
                VerifySeverity.Error => "[Error]",
                VerifySeverity.Warning => "[Warning]",
                VerifySeverity.Info => "[Info]",
                _ => "[?]",
            };
            Console.WriteLine($"{prefix} {msg.Message}");
        }
    }
}
