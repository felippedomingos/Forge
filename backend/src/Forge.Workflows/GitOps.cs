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

    // docs/010-Plugins.md §5 - the Azure DevOps acceptance test for the (aspirational,
    // never actually built as a real interface) IGitProviderPlugin shape. In practice
    // this mirrors RunGhAsync's own procedural pattern rather than that interface -
    // that's the honest finding: GitHub's "plugin" was never a real IGitProviderPlugin
    // implementation either, just inline gh/git calls, so Azure DevOps follows the
    // same actual shape instead of building an abstraction neither provider uses yet.
    // Requires `az login` + the `azure-devops` extension - an interactive step only a
    // human can complete, same as ADR-0005's gh device-flow auth.
    public static async Task<GitCommandResult> RunAzAsync(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "az",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start az process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new GitCommandResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    public record AzureRepoRef(string Organization, string Project, string Repository);

    // Parses the two URL shapes Azure Repos actually issues - HTTPS
    // ("https://dev.azure.com/{org}/{project}/_git/{repo}") and SSH
    // ("git@ssh.dev.azure.com:v3/{org}/{project}/{repo}") - so `az repos pr create` can
    // be pointed at the right org/project/repo explicitly instead of relying on
    // whatever `az devops configure -d` happens to default to on this machine (fragile,
    // and wrong the moment a second Azure DevOps project exists). Returns null on
    // anything else - callers fall back to the machine-wide default rather than fail.
    public static AzureRepoRef? TryParseAzureRepo(string repositoryUrl)
    {
        try
        {
            if (repositoryUrl.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase)
                && repositoryUrl.Contains("_git/", StringComparison.OrdinalIgnoreCase))
            {
                var httpsPart = repositoryUrl.Split("dev.azure.com/", 2)[1].TrimEnd('/');
                var segments = httpsPart.Split("/_git/");
                var orgProject = segments[0].Split('/');
                if (orgProject.Length >= 2 && segments.Length == 2)
                    return new AzureRepoRef(orgProject[0], orgProject[1], segments[1]);
            }

            if (repositoryUrl.StartsWith("git@ssh.dev.azure.com:v3/", StringComparison.OrdinalIgnoreCase))
            {
                var afterV3 = repositoryUrl["git@ssh.dev.azure.com:v3/".Length..].TrimEnd('/');
                var parts = afterV3.Split('/');
                if (parts.Length == 3)
                    return new AzureRepoRef(parts[0], parts[1], parts[2]);
            }
        }
        catch (IndexOutOfRangeException) { }

        return null;
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
