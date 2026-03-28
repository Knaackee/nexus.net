namespace Nexus.Core.Agents;

public enum AgentState
{
    Created,
    Idle,
    Running,
    WaitingForApproval,
    WaitingForInput,
    WaitingForRemoteAgent,
    Paused,
    Completed,
    Failed,
    Disposed
}
