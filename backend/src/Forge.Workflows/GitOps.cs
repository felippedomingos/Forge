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
    // Founder-requested (2026-08-07, docs/010-Plugins.md §6): `ghToken`, when set,
    // overrides the Worker process's own ambient `GH_TOKEN` for this one subprocess
    // call only - Project.GitCredential, not a single global env var shared by every
    // project. Null/empty falls back to whatever the process already has (unchanged
    // behavior for a project with no credential configured).
    public static async Task<GitCommandResult> RunGhAsync(string workingDirectory, string? ghToken, params string[] args)
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
        if (!string.IsNullOrWhiteSpace(ghToken)) psi.Environment["GH_TOKEN"] = ghToken;

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
    // `azurePat`, when set, overrides the process's ambient `AZURE_DEVOPS_EXT_PAT` for
    // this one subprocess call only - same per-project reasoning as RunGhAsync's ghToken.
    public static async Task<GitCommandResult> RunAzAsync(string workingDirectory, string? azurePat, params string[] args)
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
        if (!string.IsNullOrWhiteSpace(azurePat)) psi.Environment["AZURE_DEVOPS_EXT_PAT"] = azurePat;

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start az process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new GitCommandResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    // Founder-requested (2026-08-07): "a autenticacao de Github e Azure Devops deveria
    // ser por projeto" - found live the same day that a `git fetch` failure (an
    // expired PAT baked directly into the on-disk remote URL, entirely outside Forge's
    // knowledge) crashed a task's whole workflow after retries exhausted
    // (AgentActivities.DevelopAsync). Rather than trusting whatever credential happens
    // to already be configured on disk, Forge now actively rewrites the `origin`
    // remote's URL from Project.RepositoryUrl (the clean, credential-free source of
    // truth) plus Project.GitCredential immediately before any fetch/push - so
    // rotating the PAT in a project's settings actually takes effect on the next git
    // operation, rather than requiring someone to SSH into the host and fix
    // `.git/config` by hand. A no-op when GitCredential is null (a project with no
    // stored credential keeps relying on whatever's already configured - SSH keys,
    // manually-managed HTTPS credentials - exactly today's behavior) or when
    // RepositoryUrl isn't HTTPS (an SSH remote authenticates via host-configured keys,
    // a PAT wouldn't apply).
    public static async Task EnsureAuthenticatedRemoteAsync(string workingDirectory, string repositoryUrl, string? gitCredential)
    {
        if (string.IsNullOrWhiteSpace(gitCredential)) return;

        var authenticatedUrl = BuildAuthenticatedUrl(repositoryUrl, gitCredential);
        if (authenticatedUrl is null) return;

        await RunAsync(workingDirectory, "remote", "set-url", "origin", authenticatedUrl);
    }

    // "x-access-token" as the username is GitHub's own documented convention for
    // PAT-over-HTTPS auth; Azure DevOps accepts any non-empty username with the PAT as
    // the password over HTTPS Basic Auth, so one placeholder works for both providers
    // without needing to branch on which one this project uses.
    //
    // Deliberately plain string slicing, NOT UriBuilder(Uri)/uri.ToString() - found
    // live while testing this: round-tripping an Azure DevOps URL through UriBuilder
    // silently decodes its "%20" (a literal space in "Projetos Paralelos") back to a
    // raw space in the reconstructed string, which would have broken `git remote
    // set-url` (or worse, silently pointed it at a mangled URL) the first time this
    // ran against a real project. Slicing preserves the original path's encoding
    // byte-for-byte since it never gets re-parsed/re-escaped.
    private static string? BuildAuthenticatedUrl(string repositoryUrl, string gitCredential)
    {
        const string prefix = "https://";
        if (!repositoryUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

        var afterScheme = repositoryUrl[prefix.Length..];
        // Strip any credential already embedded in the stored URL (defensive - the
        // stored RepositoryUrl is expected to be clean, but a stale embedded
        // credential must never survive into the rewritten URL if one sneaks in).
        var atIndex = afterScheme.IndexOf('@');
        var hostAndPath = atIndex >= 0 ? afterScheme[(atIndex + 1)..] : afterScheme;

        return $"{prefix}x-access-token:{Uri.EscapeDataString(gitCredential)}@{hostAndPath}";
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
