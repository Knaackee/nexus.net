# Priority-1 Change Set Fuer Strukturierte Thinking-, Tool-Use- und Ask-User-Events

## Ziel

Dieses Dokument konkretisiert die Aenderungen fuer Prioritaet 1. Ziel ist es, Thinking beziehungsweise Reasoning, Tool Use und Ask-User-Interaktionen end-to-end strukturiert durch Nexus zu transportieren, statt Inhalte auf Plaintext oder rein implizite Nebenpfade zu reduzieren.

## Kernerkenntnisse

### 1. Reasoning ist technisch bereits angelegt, aber nicht durchgezogen

- Nexus kennt bereits `ReasoningChunkEvent`.
- Der aktive Streaming-Pfad mappt Reasoning jedoch nicht bis in AG-UI und Persistenz.
- Der finale Assistant-Turn wird aktuell erneut zu Text zusammengezogen, wodurch strukturierte Inhalte verloren gehen.

### 2. Tool Use ist vorhanden, aber im falschen Moment sichtbar

- Tool-Start und Tool-Ende existieren bereits als strukturierte Events.
- `ToolCallStartedEvent` wird heute erst nach dem vollstaendigen Modellstream emittiert, obwohl der Tool-Call in der Streaming-Antwort oft schon frueher strukturiert vorliegt.
- Fuer UI-Integrationen ist das zu spaet.

### 3. Ask User braucht in Prio 1 einen eigenen Nexus-Eventpfad

- `ask_user` existiert bereits als Tool.
- In der verwendeten `Microsoft.Extensions.AI.Abstractions`-Version 9.5.0 ist `TextReasoningContent` verifizierbar verfuegbar.
- Fuer generische Input-Request-Content-Typen gibt es im verifizierten Projektstand keinen belastbaren Beleg, auf den wir Prio 1 sicher stuetzen sollten.
- Deshalb sollte `ask_user` in Prio 1 als Nexus-eigener strukturierter Event modelliert werden, nicht als Plaintext.

### 4. Die groesste strukturelle Luecke ist nicht nur der Stream, sondern auch die Persistenz

- Session-Transcripts speichern aktuell nur `Role` und `Text`.
- Damit gehen Thinking, strukturierte Assistant-Contents und kuenftige weitere Part-Typen beim Persistieren verloren.
- Ohne Persistenzanpassung bleibt jede Streaming-Verbesserung unvollstaendig.

## Empfohlene Architekturentscheidungen

### Reasoning

- Kein Parsing von `<think>` oder aehnlichen Markern.
- Kein Nexus-Spezialformat erfinden, wenn `Microsoft.Extensions.AI` bereits `TextReasoningContent` anbietet.
- `TextReasoningContent` im Streaming erkennen und als `ReasoningChunkEvent` weiterreichen.

### Tool Use

- Bestehende Tool-Events behalten.
- `ToolCallStartedEvent` direkt dann emittieren, wenn der strukturierte Tool-Call im Streaming-Update auftaucht.
- Tool Use nicht erst nach dem vollstaendigen Stream materialisieren.

### Ask User

- Nicht als normaler Assistant-Text behandeln.
- Nicht nur auf Tool-Result-Stringdarstellung vertrauen.
- In Prio 1 als eigener Nexus-Event modellieren, etwa `UserInputRequestedEvent`.

### Persistenz

- Assistant-Nachrichten nicht nur als Text persistieren.
- `ChatMessage.Contents` in Session-Transcripts mit ablegen.
- Rueckwaertskompatiblen Fallback fuer alte Text-only-Transcripts beibehalten.

## Konkrete Aenderungen Nach Datei

### 1. `src/Nexus.Orchestration/ChatAgent.cs`

Dies ist die wichtigste Datei fuer Prio 1.

#### Problem

- Im Streaming werden aktuell im Wesentlichen nur `update.Text` und `FunctionCallContent` ausgewertet.
- Andere strukturierte `AIContent`-Typen werden nicht systematisch verarbeitet.
- Die finale Assistant-Nachricht wird aus `responseText` plus Function Calls zusammengebaut und verliert dadurch weitere strukturierte Inhalte.

#### Aenderungen

- Streaming-Auswertung von `ChatResponseUpdate` auf `update.Contents` als primaere Quelle umstellen.
- `TextContent` auf `TextChunkEvent` abbilden.
- `TextReasoningContent` auf `ReasoningChunkEvent` abbilden.
- `FunctionCallContent` sofort auf `ToolCallStartedEvent` abbilden.
- Wenn `FunctionCallContent.Name == "ask_user"`, zusaetzlich `UserInputRequestedEvent` mit strukturierter Frage und Optionen emittieren.
- Finale Assistant-Nachricht nicht nur aus Text neu aufbauen, sondern aus den tatsaechlich empfangenen `AIContent`-Inhalten zusammensetzen.

#### Ergebnis

- Thinking, Tool Use und Ask User werden waehrend des Streams sichtbar.
- Der finale Assistant-Turn behaelt seine Struktur.

### 2. `src/Nexus.Core/Events/AgentEvents.cs`

#### Problem

- Reasoning ist vorhanden, Ask User aber noch nicht als eigener strukturierter Event.

#### Aenderungen

- Bestehende Events beibehalten.
- Neue Events einfuehren:
  - `UserInputRequestedEvent`
  - optional spaeter `UserInputReceivedEvent`

#### Ergebnis

- Ask User wird auf Event-Ebene genauso ernst genommen wie Reasoning und Tool Use.

### 3. `src/Nexus.Core/Agents/AgentResult.cs`

#### Problem

- `AgentResult` traegt heute nur `Text`, `StructuredOutput`, `Metadata`, Token Usage und Cost.
- Es fehlt ein strukturierter Kanal fuer finale Assistant-Contents.

#### Aenderungen

- `IReadOnlyList<AIContent>? Contents` hinzufuegen.
- `Success(...)` so erweitern, dass neben Text optional strukturierte Contents transportiert werden koennen.

#### Ergebnis

- Der Agent Loop kann strukturierte Assistant-Nachrichten unveraendert persistieren.

### 4. `src/Nexus.AgentLoop/DefaultAgentLoop.cs`

#### Problem

- Nach Turn-Abschluss wird nur `completedResult.Text` als Assistant-Message gespeichert.
- Dadurch wird strukturierter Output erneut zu Plaintext reduziert.

#### Aenderungen

- Wenn `completedResult.Contents` vorhanden sind, `new ChatMessage(ChatRole.Assistant, completedResult.Contents)` persistieren.
- Sonst Text-Fallback beibehalten.
- Neue `UserInputRequestedEvent`-Faelle in Loop-Events uebersetzen.

#### Ergebnis

- Struktur geht beim Session-History-Aufbau nicht wieder verloren.

### 5. `src/Nexus.AgentLoop/AgentLoopAbstractions.cs`

#### Problem

- Es gibt Loop-Events fuer Text, Reasoning, Tool Calls und Approval, aber nicht fuer Ask User.

#### Aenderungen

- `UserInputRequestedLoopEvent` hinzufuegen.
- Optional spaeter `UserInputReceivedLoopEvent` ergaenzen.

#### Ergebnis

- Ask User kann im Session- und UI-Layer separat verarbeitet werden.

### 6. `src/Nexus.Protocols.AgUi/AgUiEvent.cs`

#### Problem

- AG-UI kennt aktuell keine Reasoning- oder Ask-User-Events.

#### Aenderungen

- Neue AG-UI-Events einfuehren:
  - `AgUiReasoningChunkEvent`
  - `AgUiApprovalRequestedEvent`
  - `AgUiUserInputRequestEvent`

#### Ergebnis

- Continue oder andere Frontends erhalten Thinking und Interaktionen als first-class Events.

### 7. `src/Nexus.Protocols.AgUi/AgUiEventBridge.cs`

#### Problem

- Die Bridge mappt aktuell nur Text und Tool-Events.

#### Aenderungen

- `ReasoningChunkEvent` auf `AgUiReasoningChunkEvent` mappen.
- `ApprovalRequestedEvent` auf `AgUiApprovalRequestedEvent` mappen.
- `UserInputRequestedEvent` auf `AgUiUserInputRequestEvent` mappen.

#### Ergebnis

- Die neuen strukturierten Ereignisse kommen tatsaechlich im Frontend an.

### 8. `src/Nexus.Hosting.AspNetCore/Endpoints/Endpoints.cs`

#### Problem

- Der Serializer-Kontext kennt neue AG-UI-Typen noch nicht.

#### Aenderungen

- Alle neuen AG-UI-Events als `JsonDerivedType` registrieren.

#### Ergebnis

- Neue Events werden korrekt per SSE serialisiert.

### 9. `src/Nexus.Sessions/FileSessionStore.cs`

#### Problem

- `PersistedChatMessage` speichert nur `Role` und `Text`.
- Strukturierte `Contents` gehen verloren.

#### Aenderungen

- Persistiertes Modell auf `Role`, `Text` und `Contents` erweitern.
- Beim Lesen:
  - wenn `Contents` vorhanden sind, strukturierte `ChatMessage` rekonstruieren
  - sonst bestehenden Text-Fallback verwenden

#### Ergebnis

- Session-Transcripts bleiben mit der neuen Struktur kompatibel.

### 10. `src/Nexus.Tools.Standard/AskUserTool.cs`

#### Problem

- Das Tool ist funktional in Ordnung, aber der strukturierte Charakter der Interaktion wird ausserhalb des Tools nicht sichtbar.

#### Aenderungen

- Fuer Prio 1 keine grosse Aenderung im Tool selbst erzwingen.
- Optional Input-Schema spaeter ausbauen.
- Die eigentliche Strukturierung im Event- und Streaming-Pfad von `ChatAgent` vornehmen.

#### Ergebnis

- Minimalinvasiver Prio-1-Umfang ohne unnötige Tool-Neudesigns.

## Tests, Die Fuer Prio 1 Ergaenzt Werden Sollten

### `tests/Nexus.Orchestration.Tests/ChatAgentTests.cs`

- Streaming mit `TextReasoningContent` emittiert `ReasoningChunkEvent`.
- Streaming mit `FunctionCallContent` emittiert `ToolCallStartedEvent` fruehzeitig.
- `ask_user`-Tool-Call emittiert `UserInputRequestedEvent`.
- Finale Assistant-Nachricht behaelt strukturierte Contents.

### `tests/Nexus.AgentLoop.Tests/AgentLoopTests.cs`

- Strukturierte Assistant-Contents werden nach Turn-Abschluss nicht zu Plaintext degradiert.
- `UserInputRequestedEvent` wird in `UserInputRequestedLoopEvent` uebersetzt.
- Approval- und Ask-User-Pfaade koennen nebeneinander bestehen.

### `src/Nexus.Testing/Mocks/FakeChatClient.cs`

- Helper fuer Reasoning-Updates ergaenzen.
- Helper fuer gemischte Streaming-Updates aus Text, Reasoning und Function Calls ergaenzen.

## Offene Fragen Mit Entscheidungsvorschlag

### 1. Soll `ask_user` als Part oder als Event modelliert werden?

Vorschlag:

- In Prio 1 als Event modellieren.
- Begruendung: Die Interaktion ist host- und workflow-naher als ein normaler Assistant-Content-Block.
- Das ist mit dem heutigen Nexus-Code einfacher und sicherer anschlussfaehig.

### 2. Soll `ApprovalRequestedEvent` mit `ask_user` vereinheitlicht werden?

Vorschlag:

- Nein, noch nicht.
- Approval und Ask User sind verwandt, aber semantisch verschieden.
- Beide koennen spaeter unter einem allgemeinen Human-interaction-Modell zusammengefuehrt werden, muessen es fuer Prio 1 aber nicht.

### 3. Soll Prio 1 bereits ein generisches neues Nexus-Part-Modell einfuehren?

Vorschlag:

- Nein, kein grosses separates Modell einfuehren, solange `Microsoft.Extensions.AI` die noetigen Inhaltstypen bereits liefert.
- Fuer Prio 1 bestehende `AIContent`-Typen plus wenige Nexus-eigene Events nutzen.

## Definition of Done Fuer Prio 1

- Reasoning wird als eigener strukturierter Stream-Pfad transportiert.
- Tool Use wird waehrend des Streams strukturiert sichtbar.
- Ask User wird nicht als normaler Assistant-Text behandelt, sondern als eigener Event transportiert.
- AG-UI kann Reasoning, Tool Use und Ask User separat konsumieren.
- Session-Transcripts verlieren strukturierte Assistant-Inhalte nicht mehr.
- Mindestens ein End-to-End-Test deckt Reasoning plus Tool Use ab.
- Mindestens ein End-to-End-Test deckt Ask User oder Approval-nahe Interaktion ab.

## Nicht Teil Von Prio 1

- `refusal`
- `citation` oder `annotation`
- `image`
- `document`
- `audio`
- grosses generisches Redesign aller Message-Typen ohne unmittelbaren Prio-1-Nutzen

## Empfohlene Umsetzungsreihenfolge

1. `ChatAgent` und `AgentEvents` anpassen.
2. `AgentResult` und `DefaultAgentLoop` auf strukturierte Contents erweitern.
3. `AgUiEvent`, `AgUiEventBridge` und ASP.NET-Serializer nachziehen.
4. `FileSessionStore` rueckwaertskompatibel erweitern.
5. Test-Mocks und Unit-Tests ergaenzen.
