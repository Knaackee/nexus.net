# Quick Start

> **See the full [Quick Start Guide](../guides/quick-start.md)** for step-by-step instructions.

## Minimal Agent in 30 Seconds

```bash
dotnet new console -n MyAgent
cd MyAgent
dotnet add package Nexus.Core
dotnet add package Nexus.Orchestration
dotnet add package Nexus.Memory
dotnet add package Nexus.Defaults
```

```csharp
using Microsoft.Extensions.AI;
using Nexus;

var host = Nexus.Nexus.CreateDefault(new OllamaChatClient(new Uri("http://localhost:11434"), "qwen3"));
await foreach (var evt in host.RunAsync("What is the capital of France?"))
{
    // Handle streaming events
}
await host.DisposeAsync();
```

## Next Steps

- [Installation](installation.md) — Full package list and combinations
- [Quick Start Guide](../guides/quick-start.md) — Detailed walkthrough with tools and orchestration
- [Examples Index](../../examples/README.md) — Runnable scenario examples
