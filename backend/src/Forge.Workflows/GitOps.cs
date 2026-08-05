using System.Diagnostics;

namespace Forge.Workflows;

public record GitCommandResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Success => ExitCode == 0;
}

// docs/007-ExecutionEngine.md §2 - the git plumbing behind worktree sync/create.
// Plain `git` subprocess calls, not LibGit2Sharp - simplest thing that works, and the
// Developer agent's own coding loop already shells out via ClaudeCliProvider, so this
// keeps a consistent "subprocess, not a library" shape across the activity.
public static class GitOps
{
    public static async Task<GitCommandResult> RunAsync(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new GitCommandResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    // docs/005-Agents.md §6 / docs/010-Plugins.md §2 - PR creation via the `gh` CLI
    // rather than a hand-rolled GitHub REST call. Same subprocess shape as RunAsync.
    public static async Task<GitCommandResult> RunGhAsync(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "gh",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start gh process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new GitCommandResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    // docs/007-ExecutionEngine.md §2 branch naming convention.
    public static string Slugify(string title)
    {
        var lowered = title.ToLowerInvariant();
        var chars = lowered.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        return slug.Length > 40 ? slug[..40].TrimEnd('-') : slug;
    }
}
