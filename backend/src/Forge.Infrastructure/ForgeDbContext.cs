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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // docs/011-Database.md §2 - table/column names in snake_case to match the SQL there.
        modelBuilder.Entity<Project>(e =>
        {
            e.ToTable("projects");
            e.HasKey(p => p.Id);
            e.Property(p => p.PublishRecipe).HasColumnType("jsonb");
        });

        modelBuilder.Entity<TaskItem>(e =>
        {
            e.ToTable("tasks");
            e.HasKey(t => t.Id);
            // docs/011-Database.md §2: `state text NOT NULL` - stored as its enum name,
            // not the default int, so the column matches the documented schema and isn't
            // silently corrupted if TaskState's member order ever changes.
            e.Property(t => t.State).HasConversion<string>();
            e.HasIndex(t => new { t.ProjectId, t.State }).HasDatabaseName("ix_tasks_project_state");
            e.HasIndex(t => new { t.ProjectId, t.Priority }).HasDatabaseName("ix_tasks_project_priority");
            e.HasOne(t => t.Project).WithMany(p => p.Tasks).HasForeignKey(t => t.ProjectId);
            e.HasOne(t => t.Worktree).WithOne().HasForeignKey<TaskItem>(t => t.WorktreeId);
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
        });

        modelBuilder.Entity<DomainEvent>(e =>
        {
            e.ToTable("events");
            e.HasKey(ev => ev.Id);
            e.Property(ev => ev.Payload).HasColumnType("jsonb");
            e.HasIndex(ev => new { ev.TaskId, ev.OccurredAt }).HasDatabaseName("ix_events_task_time");
            e.HasOne(ev => ev.Task).WithMany(t => t.Events).HasForeignKey(ev => ev.TaskId);
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
