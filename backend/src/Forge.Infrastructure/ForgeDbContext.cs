using Forge.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Forge.Infrastructure;

// Owns only the `forge` database - never `temporal`/`temporal_visibility`, which are
// exclusively managed by Temporal's own auto-setup tooling. See docs/011-Database.md §1.
public class ForgeDbContext(DbContextOptions<ForgeDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<SubTask> SubTasks => Set<SubTask>();
    public DbSet<AcceptanceCriterion> AcceptanceCriteria => Set<AcceptanceCriterion>();
    public DbSet<Worker> Workers => Set<Worker>();
    public DbSet<Worktree> Worktrees => Set<Worktree>();
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<DomainEvent> Events => Set<DomainEvent>();
    public DbSet<Plugin> Plugins => Set<Plugin>();
    public DbSet<LlmModel> Models => Set<LlmModel>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AgentMemory> AgentMemories => Set<AgentMemory>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ClaudeAccount> ClaudeAccounts => Set<ClaudeAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // docs/011-Database.md §2 - table/column names in snake_case to match the SQL there.
        modelBuilder.Entity<Project>(e =>
        {
            e.ToTable("projects");
            e.HasKey(p => p.Id);
            e.Property(p => p.PublishRecipe).HasColumnType("jsonb");
            // Two projects sharing a prefix would make task tags ambiguous ("FORGE-1"
            // in which project?) - enforced here, not just convention.
            e.HasIndex(p => p.Prefix).IsUnique();
        });

        modelBuilder.Entity<TaskItem>(e =>
        {
            e.ToTable("tasks");
            e.HasKey(t => t.Id);
            // A task's tag (Project.Prefix + "-" + Number) must be unique within its
            // project - two tasks with the same number in the same project would be
            // indistinguishable in the UI.
            e.HasIndex(t => new { t.ProjectId, t.Number }).IsUnique();
            // docs/011-Database.md §2: `state text NOT NULL` - stored as its enum name,
            // not the default int, so the column matches the documented schema and isn't
            // silently corrupted if TaskState's member order ever changes.
            e.Property(t => t.State).HasConversion<string>();
            e.HasIndex(t => new { t.ProjectId, t.State }).HasDatabaseName("ix_tasks_project_state");
            e.HasIndex(t => new { t.ProjectId, t.Priority }).HasDatabaseName("ix_tasks_project_priority");
            e.HasOne(t => t.Project).WithMany(p => p.Tasks).HasForeignKey(t => t.ProjectId);
            e.HasOne(t => t.Worktree).WithOne().HasForeignKey<TaskItem>(t => t.WorktreeId).OnDelete(DeleteBehavior.SetNull);
            // Explicit join table name/columns (default would be "TagTaskItem") to match
            // docs/011-Database.md's snake_case naming and the rest of this schema.
            e.HasMany(t => t.Tags).WithMany(tag => tag.Tasks)
                .UsingEntity(j => j.ToTable("task_tags"));
        });

        modelBuilder.Entity<Tag>(e =>
        {
            e.ToTable("tags");
            e.HasKey(t => t.Id);
            e.HasOne(t => t.Project).WithMany().HasForeignKey(t => t.ProjectId);
            // Two tags with the same name in the same project would be indistinguishable
            // in the picker (TaskDetailSheet).
            e.HasIndex(t => new { t.ProjectId, t.Name }).IsUnique();
        });

        modelBuilder.Entity<SubTask>(e =>
        {
            e.ToTable("sub_tasks");
            e.HasKey(s => s.Id);
            e.HasOne(s => s.Task).WithMany(t => t.SubTasks).HasForeignKey(s => s.TaskId);
        });

        modelBuilder.Entity<AcceptanceCriterion>(e =>
        {
            e.ToTable("acceptance_criteria");
            e.HasKey(a => a.Id);
            e.HasOne(a => a.Task).WithMany(t => t.AcceptanceCriteria).HasForeignKey(a => a.TaskId);
        });

        modelBuilder.Entity<Worker>(e =>
        {
            e.ToTable("workers");
            e.HasKey(w => w.Id);
            e.Property(w => w.Status).HasConversion<string>();
            e.HasOne(w => w.CurrentTask).WithMany().HasForeignKey(w => w.CurrentTaskId);
        });

        modelBuilder.Entity<Worktree>(e =>
        {
            e.ToTable("worktrees");
            e.HasKey(w => w.Id);
            e.HasOne(w => w.Task).WithMany().HasForeignKey(w => w.TaskId);
            e.HasOne(w => w.Project).WithMany().HasForeignKey(w => w.ProjectId);
            // docs/003-Domain.md INV-2 / docs/011-Database.md §2: at most one active worktree per task.
            e.HasIndex(w => w.TaskId)
                .HasDatabaseName("ux_worktrees_active_task")
                .IsUnique()
                .HasFilter("deleted_at IS NULL");
        });

        modelBuilder.Entity<Run>(e =>
        {
            e.ToTable("runs");
            e.HasKey(r => r.Id);
            e.Property(r => r.AgentRole).HasConversion<string>();
            e.Property(r => r.Status).HasConversion<string>();
            e.Property(r => r.CostEstimate).HasColumnType("numeric(10,4)");
            e.HasIndex(r => r.TaskId).HasDatabaseName("ix_runs_task");
            e.HasOne(r => r.Task).WithMany(t => t.Runs).HasForeignKey(r => r.TaskId);
            // Optional - keep the Run's cost/token history even if the ClaudeAccount
            // that handled it is later removed, rather than losing the record.
            e.HasOne(r => r.ClaudeAccount).WithMany(a => a.Runs).HasForeignKey(r => r.ClaudeAccountId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(r => r.ClaudeAccountId).HasDatabaseName("ix_runs_claude_account");
        });

        modelBuilder.Entity<ClaudeAccount>(e =>
        {
            e.ToTable("claude_accounts");
            e.HasKey(a => a.Id);
            e.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.UserId).HasDatabaseName("ix_claude_accounts_user");
        });

        modelBuilder.Entity<DomainEvent>(e =>
        {
            e.ToTable("events");
            e.HasKey(ev => ev.Id);
            e.Property(ev => ev.Payload).HasColumnType("jsonb");
            e.HasIndex(ev => new { ev.TaskId, ev.OccurredAt }).HasDatabaseName("ix_events_task_time");
            e.HasOne(ev => ev.Task).WithMany(t => t.Events).HasForeignKey(ev => ev.TaskId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Plugin>(e =>
        {
            e.ToTable("plugins");
            e.HasKey(p => p.Id);
            e.Property(p => p.Kind).HasConversion<string>();
            e.Property(p => p.Configuration).HasColumnType("jsonb");
        });

        modelBuilder.Entity<LlmModel>(e =>
        {
            e.ToTable("models");
            e.HasKey(m => m.Id);
            e.Property(m => m.CostPerToken).HasColumnType("numeric(12,8)");
        });

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<AgentMemory>(e =>
        {
            e.ToTable("agent_memory");
            e.HasKey(a => a.Id);
            e.Property(a => a.AgentRole).HasConversion<string>();
            e.HasOne(a => a.Project).WithMany().HasForeignKey(a => a.ProjectId);
            e.HasIndex(a => new { a.ProjectId, a.AgentRole, a.Key }).IsUnique();
        });
    }
}
