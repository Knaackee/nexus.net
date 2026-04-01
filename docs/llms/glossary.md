# Glossary

- agent: an executable reasoning unit created from `AgentDefinition`
- agent loop: multi-turn runtime that can route across steps and sessions
- orchestration: sequencing or graph execution across one or more tasks
- workflow DSL: serializable workflow model that compiles to orchestration graphs
- tool: callable capability exposed through `ITool`
- approval gate: runtime checkpoint that accepts, rejects, or modifies progression context
- compaction: reducing active prompt history while preserving essential context
- session: stored transcript and metadata for ongoing chat continuity
- checkpoint: persisted orchestration snapshot for resume or recovery
- sub-agent: child agent delegated by another agent or tool invocation