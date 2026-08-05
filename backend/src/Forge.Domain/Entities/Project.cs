namespace Forge.Domain.Entities;

// docs/003-Domain.md §1 - maps 1:1 to a Git repository.
public class Project
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string RepositoryUrl { get; set; }
    public required string RootBranch { get; set; } // "main" | "develop" | "dev"
    public Guid GitProviderPluginId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Plugin? GitProviderPlugin { get; set; }
    public ICollection<TaskItem> Tasks { get; set; } = [];
}
