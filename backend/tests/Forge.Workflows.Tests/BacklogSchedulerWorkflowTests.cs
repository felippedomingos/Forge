using Xunit;

namespace Forge.Workflows.Tests;

// Exercises BacklogSchedulerWorkflow.ShouldPromote directly - the exact decision
// RunAsync makes every poll tick before signalling a task's PromoteToTodoAsync - rather
// than spinning up a full Temporal test environment for what is, at its core, a pure
// function of the per-Project snapshot.
public class BacklogSchedulerWorkflowTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void ShouldPromote_StopsAtTheConfiguredPerProjectLimit(int maxConcurrentExecuting)
    {
        var topBacklogTaskId = Guid.NewGuid();

        for (var executingCount = 0; executingCount <= maxConcurrentExecuting + 3; executingCount++)
        {
            var snapshot = new SchedulingSnapshot(
                ExecutingCount: executingCount,
                TopBacklogTaskId: topBacklogTaskId,
                UnprioritizedBacklogCount: 0,
                MaxConcurrentExecuting: maxConcurrentExecuting);

            var shouldPromote = BacklogSchedulerWorkflow.ShouldPromote(snapshot);

            if (executingCount < maxConcurrentExecuting)
                Assert.True(shouldPromote, $"expected a free slot at ExecutingCount={executingCount} of {maxConcurrentExecuting}");
            else
                Assert.False(shouldPromote, $"expected no free slot at ExecutingCount={executingCount} of {maxConcurrentExecuting}");
        }
    }

    [Fact]
    public void ShouldPromote_DrainingABacklog_NeverPushesExecutingPastTheLimit()
    {
        const int maxConcurrentExecuting = 2;
        var executingCount = 0;
        var backlogRemaining = 10;

        // Mirrors RunAsync's own loop shape: re-check the snapshot, promote one task if
        // a slot is free, otherwise wait for one to free up (a task leaving Executing).
        while (backlogRemaining > 0)
        {
            var snapshot = new SchedulingSnapshot(executingCount, Guid.NewGuid(), 0, maxConcurrentExecuting);

            if (!BacklogSchedulerWorkflow.ShouldPromote(snapshot))
            {
                executingCount--; // a slot frees up before the next tick
                continue;
            }

            executingCount++;
            backlogRemaining--;

            Assert.True(
                executingCount <= maxConcurrentExecuting,
                $"promoted a ({maxConcurrentExecuting + 1})-th task into Executing");
        }
    }
}
