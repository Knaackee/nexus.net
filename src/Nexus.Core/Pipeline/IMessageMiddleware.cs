namespace Nexus.Core.Pipeline;

public delegate Task MessageDelegate(Contracts.AgentMessage message, CancellationToken ct);

public interface IMessageMiddleware
{
    Task InvokeAsync(Contracts.AgentMessage message, MessageDelegate next, CancellationToken ct);
}
