using System.Text.RegularExpressions;

// Conventional Commits: https://www.conventionalcommits.org/en/v1.0.0/
private var pattern = new Regex(
    @"^(feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert)(\(.+\))?!?:\s.+",
    RegexOptions.Compiled);

var msg = File.ReadAllLines(Args[0]);
if (msg.Length == 0 || string.IsNullOrWhiteSpace(msg[0]))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine("  ✗ Commit message is empty.");
    Console.ResetColor();
    Environment.Exit(1);
}

var header = msg[0].Trim();

// Allow merge commits
if (header.StartsWith("Merge "))
{
    Environment.Exit(0);
}

if (!pattern.IsMatch(header))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine(
        "  ✗ Commit message does not follow Conventional Commits.");
    Console.Error.WriteLine();
    Console.ResetColor();
    Console.Error.WriteLine("  Expected format:");
    Console.Error.WriteLine(
        "    <type>[optional scope]: <description>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  Allowed types:");
    Console.Error.WriteLine(
        "    feat, fix, docs, style, refactor, perf,");
    Console.Error.WriteLine(
        "    test, build, ci, chore, revert");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  Examples:");
    Console.Error.WriteLine("    feat: add user authentication");
    Console.Error.WriteLine("    fix(api): handle null response");
    Console.Error.WriteLine("    docs: update README with usage");
    Console.Error.WriteLine();
    Console.Error.WriteLine(
        "  Spec: https://www.conventionalcommits.org/en/v1.0.0/");
    Environment.Exit(1);
}

if (header.Length > 72)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Error.WriteLine(
        $"  ⚠ Header is {header.Length} chars (max 72 recommended).");
    Console.ResetColor();
}

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("  ✓ Commit message is valid.");
Console.ResetColor();
Environment.Exit(0);
