# Nexus.Skills API Reference

`Nexus.Skills` provides reusable prompt and tool profiles that can be applied to agent definitions at runtime.

Use it when hosts should load reusable capabilities from code or markdown directories instead of hardcoding prompt and tool bundles everywhere.

## Key Types

### `SkillDefinition`

Defines:

- `Name`
- `Description`
- `SystemPrompt`
- `ToolNames`
- `ModelId`
- `WhenToUse`
- `Source`
- `SourcePath`

### `ISkillCatalog`

Main resolution surface for named and relevant skills.

```csharp
public interface ISkillCatalog
{
    SkillDefinition? Resolve(string name);
    IReadOnlyList<SkillDefinition> ListAll();
    IReadOnlyList<SkillDefinition> FindRelevant(string userMessage, IReadOnlyList<ChatMessage>? messages = null, int maxResults = 3);
}
```

### `SkillDefinitionExtensions`

Provides `ApplyTo(...)` and `BuildAgentDefinition(...)` for merging skill definitions into `AgentDefinition` instances.

### `SkillInjectionOptions`

Controls whether automatic injection is enabled and how many relevant skills can be applied.

## Registration

```csharp
services.AddNexus(builder =>
{
    builder.AddSkills(skills =>
    {
        skills.UseDefaults();
        skills.AddDirectory(".nexus/skills", SkillSource.Project);
    });
});
```

## What `UseDefaults()` Does

- registers the markdown skill loader
- registers the skill-injection middleware
- enables a shared in-memory skill catalog

## When To Use It

- prompts and tool bundles should be reusable across sessions
- project and user skill directories should be loadable from disk
- hosts want automatic relevance-based skill selection

## Related Packages

- `Nexus.Commands`
- `Nexus.AgentLoop`
- `Nexus.Defaults`

## Related Docs

- [Nexus CLI](../examples/nexus-cli.md)