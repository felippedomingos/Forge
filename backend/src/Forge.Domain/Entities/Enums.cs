namespace Forge.Domain.Entities;

// Mirrors docs/003-Domain.md §3 exactly - do not add states here without updating that document first.
public enum TaskState
{
    Inbox,
    Backlog,
    Blocked,
    Todo,
    Executing,
    AwaitingPublish,
    Publishing,
    Review,
    Done,
    Production
}

// Mirrors docs/005-Agents.md - the 5 LLM-driven roles. The Backlog->Todo promotion is
// deliberately NOT here: it's deterministic scheduler logic (docs/003-Domain.md §3, docs/006-Scheduler.md).
public enum AgentRole
{
    Planner,
    Prioritizer,
    Developer,
    Deploy,
    Git
}

public enum RunStatus
{
    Success,
    Failed,
    Timeout
}

public enum WorkerStatus
{
    Idle,
    Busy,
    Offline
}

// Mirrors docs/010-Plugins.md §1.
public enum PluginKind
{
    GitProvider,
    IssueTracker,
    CloudCli,
    Database,
    DeploymentTarget
}
