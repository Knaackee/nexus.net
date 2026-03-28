using System.Runtime.CompilerServices;
using Nexus.Core.Agents;
using Nexus.Core.Events;

namespace Nexus.Core.Pipeline;

public sealed class AgentPipelineBuilder
{
    private readonly List<IAgentMiddleware> _middlewares = [];

    public AgentPipelineBuilder Use(IAgentMiddleware middleware)
    {
        _middlewares.Add(middleware);
        return this;
    }

    public AgentExecutionDelegate BuildBuffered(AgentExecutionDelegate terminal)
    {
        var pipeline = terminal;
        for (int i = _middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var next = pipeline;
            pipeline = (task, ctx, ct) => middleware.InvokeAsync(task, ctx, next, ct);
        }
        return pipeline;
    }

    public StreamingAgentExecutionDelegate BuildStreaming(StreamingAgentExecutionDelegate terminal)
    {
        var pipeline = terminal;
        for (int i = _middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var next = pipeline;
            pipeline = (task, ctx, ct) => middleware.InvokeStreamingAsync(task, ctx, next, ct);
        }
        return pipeline;
    }
}

public sealed class ToolPipelineBuilder
{
    private readonly List<IToolMiddleware> _middlewares = [];

    public ToolPipelineBuilder Use(IToolMiddleware middleware)
    {
        _middlewares.Add(middleware);
        return this;
    }

    public ToolExecutionDelegate BuildBuffered(ToolExecutionDelegate terminal)
    {
        var pipeline = terminal;
        for (int i = _middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var next = pipeline;
            pipeline = (tool, input, ctx, ct) => middleware.InvokeAsync(tool, input, ctx, next, ct);
        }
        return pipeline;
    }

    public StreamingToolExecutionDelegate BuildStreaming(StreamingToolExecutionDelegate terminal)
    {
        var pipeline = terminal;
        for (int i = _middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var next = pipeline;
            pipeline = (tool, input, ctx, ct) => middleware.InvokeStreamingAsync(tool, input, ctx, next, ct);
        }
        return pipeline;
    }
}
