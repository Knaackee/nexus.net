using Nexus.Core.Agents;
using Nexus.Core.Events;

namespace Nexus.Orchestration;

public abstract record OrchestrationEvent(TaskGraphId GraphId, DateTimeOffset Timestamp);

public record NodeStartedEvent(TaskGraphId GraphId, TaskId NodeId, AgentId AgentId)
    : OrchestrationEvent(GraphId, DateTimeOffset.UtcNow);

public record NodeCompletedEvent(TaskGraphId GraphId, TaskId NodeId, AgentResult Result)
    : OrchestrationEvent(GraphId, DateTimeOffset.UtcNow);

public record NodeFailedEvent(TaskGraphId GraphId, TaskId NodeId, Exception Error)
    : OrchestrationEvent(GraphId, DateTimeOffset.UtcNow);

public record NodeSkippedEvent(TaskGraphId GraphId, TaskId NodeId, string Reason)
    : OrchestrationEvent(GraphId, DateTimeOffset.UtcNow);

public record AgentEventInGraph(TaskGraphId GraphId, TaskId NodeId, AgentEvent InnerEvent)
    : OrchestrationEvent(GraphId, DateTimeOffset.UtcNow);

public record CheckpointCreatedEvent(TaskGraphId GraphId, CheckpointId CheckpointId)
    : OrchestrationEvent(GraphId, DateTimeOffset.UtcNow);

public record OrchestrationCompletedEvent(TaskGraphId GraphId, OrchestrationResult Result)
    : OrchestrationEvent(GraphId, DateTimeOffset.UtcNow);

public record AgentLifecycleEvent(AgentId AgentId, AgentState OldState, AgentState NewState, DateTimeOffset Timestamp);
