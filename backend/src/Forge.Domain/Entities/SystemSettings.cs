namespace Forge.Domain.Entities;

// Founder-requested (2026-08-08) - closes docs/001-Requirements.md NFR-1's last open
// gap: a global concurrency ceiling across every project's Executing tasks combined,
// not just the per-project Project.MaxConcurrentExecuting cap. Singleton table (one
// row, seeded by migration) rather than a key/value settings store - there is
// currently exactly one global setting worth exposing, and inventing a generic
// key/value schema for a single int would be exactly the premature abstraction
// docs/000-Vision.md's engineering priorities warn against. Revisit if a second
// global setting shows up.
public class SystemSettings
{
    public Guid Id { get; set; }
    // Default 6, founder-specified. Enforced atomically alongside the per-project
    // check in SchedulingActivities.HasExecutingCapacityAsync - both checks share the
    // same advisory-lock transaction, closing the same TOCTOU race the per-project
    // check itself was fixed for earlier the same day.
    public int MaxGlobalConcurrentExecuting { get; set; } = 6;
}
