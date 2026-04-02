# Continue Team Plan Fuer Nexus Priority 1

## Ziel

Continue soll die neuen strukturierten Nexus-Events fuer Reasoning, Tool Use, Approval und Ask User bereits jetzt konsumieren koennen, damit die Integration parallel zur Nexus-Implementierung vorangetrieben werden kann.

## Was Nexus In Priority 1 Liefert

Der relevante AG-UI-Stream enthaelt fuer Priority 1 folgende Ereignisse:

- `AgUiTextChunkEvent` fuer sichtbaren Assistant-Text
- `AgUiReasoningChunkEvent` fuer Thinking beziehungsweise Reasoning
- `AgUiToolCallStartEvent` fuer strukturierte Tool-Aufrufe
- `AgUiToolCallEndEvent` fuer Tool-Ergebnisse
- `AgUiApprovalRequestedEvent` fuer Freigabeanforderungen
- `AgUiUserInputRequestEvent` fuer strukturierte Rueckfragen an den Nutzer

## Erwartetes UI-Verhalten

### 1. Reasoning getrennt von Text rendern

- `AgUiReasoningChunkEvent` nicht in den normalen Antworttext mischen.
- Reasoning als eigenes UI-Element oder Thinking-Bereich anzeigen.
- `AgUiTextChunkEvent` weiterhin als sichtbare finale Antwort behandeln.

### 2. Tool Use als strukturierte Aktivitaet anzeigen

- `AgUiToolCallStartEvent` als Tool-Aktivitaet oder Tool-Card rendern.
- `AgUiToolCallEndEvent` dem passenden Tool-Call zuordnen.
- Tool Use nicht nur aus Text rekonstruieren.

### 3. Approval und Ask User getrennt behandeln

- `AgUiApprovalRequestedEvent` als explizite Freigabeanforderung rendern.
- `AgUiUserInputRequestEvent` als inhaltliche Rueckfrage rendern.
- Approval und Ask User nicht in denselben UI-Zustand zusammenwerfen.

## Event-Details

### `AgUiReasoningChunkEvent`

- Feld: `text`
- Bedeutung: Nicht-finaler oder begleitender Reasoning-Text des Modells
- UI-Hinweis: In Thinking-Bereich akkumulieren

### `AgUiToolCallStartEvent`

- Felder: `toolCallId`, `toolName`, `arguments`
- Bedeutung: Modell hat einen Tool-Aufruf strukturiert begonnen
- UI-Hinweis: Tool-Aktivitaet mit Pending-Status anlegen

### `AgUiToolCallEndEvent`

- Felder: `toolCallId`, `result`
- Bedeutung: Tool-Aufruf wurde beendet
- UI-Hinweis: Vorherigen Pending-Tool-Eintrag abschliessen

### `AgUiApprovalRequestedEvent`

- Felder: `approvalId`, `description`
- Bedeutung: Nexus benoetigt eine Freigabe vor der Fortsetzung
- UI-Hinweis: Freigabedialog oder Approval-Card anzeigen

### `AgUiUserInputRequestEvent`

- Felder:
  - `requestId`
  - `request.inputType`
  - `request.question`
  - `request.options`
  - `request.placeholder`
  - `request.isOptional`
  - `request.timeoutSeconds`
  - `request.reason`
- Bedeutung: Agent fordert strukturiert eine Nutzereingabe an
- UI-Hinweis: Passendes Eingabeelement nach `inputType` rendern

## Vorgeschlagene Continue-Aufgaben

### 1. Event-Parser erweitern

- Neue AG-UI-Events in den Stream-Parser aufnehmen.
- Reasoning, Approval und User Input Request nicht als unbekannte Custom-Events behandeln.

### 2. UI-State erweitern

- Separaten State fuer Thinking-Chunks fuehren.
- Tool-Calls ueber `toolCallId` korrelieren.
- Approval- und Ask-User-Requests als interaktive Pending-States modellieren.

### 3. Rendering erweitern

- Thinking-Panel oder collapsible Thinking-Sektion fuer `AgUiReasoningChunkEvent`.
- Tool-Use-Elemente fuer Start und Ende.
- Approval-UI und Ask-User-UI getrennt auspraegen.

### 4. Fallback-Verhalten festlegen

- Wenn `AgUiReasoningChunkEvent` fehlt, normalen Textfluss weiter wie bisher behandeln.
- Kein `<think>`-Parsing als Hauptweg nutzen, solange Nexus die Struktur liefert.

## Testfaelle Fuer Continue

### Reasoning

- Stream mit `AgUiReasoningChunkEvent` und `AgUiTextChunkEvent`
- Erwartung: Thinking separat, finaler Text separat

### Tool Use

- Stream mit `AgUiToolCallStartEvent`, `AgUiToolCallEndEvent` und Text
- Erwartung: Tool-Aktivitaet wird nicht in normalen Chat-Text gemischt

### Approval

- Stream mit `AgUiApprovalRequestedEvent`
- Erwartung: Freigabe-UI statt normaler Assistant-Nachricht

### Ask User

- Stream mit `AgUiUserInputRequestEvent`
- Erwartung: Strukturiertes Eingabeelement passend zum `inputType`

## Abgrenzung

Nicht Teil dieses ersten Continue-Schritts:

- `refusal`
- `citation` beziehungsweise `annotation`
- multimodale Antworttypen wie `image`, `document`, `audio`

Diese folgen erst nach dem ersten Nexus-Release fuer Priority 1.