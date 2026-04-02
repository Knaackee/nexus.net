# Plan Fuer Strukturierte Thinking- und Reasoning-Parts in Nexus

## Ziel

Nexus soll Thinking und Reasoning, sofern vom Modell strukturiert verfuegbar, als eigene Stream-Parts transportieren statt als normalen Antworttext. Darueber hinaus soll Nexus weitere semantisch eigenstaendige Inhalte nicht in Plaintext flatten, wenn Provider diese bereits strukturiert liefern. Continue soll diese Inhalte dadurch als separate UI-Elemente rendern koennen, waehrend der finale Assistant-Text nur die eigentliche Nutzerantwort enthaelt.

## Problemzuschnitt

Im aktuellen Pfad wird Thinking offenbar nicht als eigener strukturierter Part weitergegeben, sondern als zusammenhaengender Textstrom. Dadurch landet der Inhalt im normalen Antworttext und kann von Continue nicht als Thinking-Element erkannt werden.

Die Analyse zeigt zusaetzlich, dass der aktuelle Nexus-Pfad an mehreren Stellen insgesamt textzentriert ist. Neben Thinking koennen dadurch auch weitere strukturierte Inhalte wie Refusals, Annotations oder multimodale Inhalte ihre Semantik verlieren, wenn sie nur als Text oder nur teilweise transportiert werden.

## Erweiterter Strukturierungsbedarf

Neben `thinking` beziehungsweise `reasoning` verdienen weitere Inhaltstypen einen eigenen Typ, wenn sie vom Provider strukturiert geliefert werden:

- `refusal` fuer explizite Ablehnungen oder Policy-bezogene Verweigerungen.
- `citation` oder `annotation` fuer Quellen, Fundstellen und textbezogene Metadaten.
- `image` und `document` fuer multimodale Inhalte statt Textplatzhaltern oder eingebettetem Plaintext.
- `audio` fuer gesprochene oder transkribierte Ausgaben, falls spaeter unterstuetzt.
- `tool_use` und provider-spezifische Tool-Ergebnis-Typen, sofern der Stream diese als eigene Bloecke liefert.
- `ask_user` beziehungsweise allgemeine Human-interaction-Anforderungen, wenn Nexus den Nutzer nicht nur mit Freitext, sondern mit einer strukturierten Rueckfrage oder Freigabe ansprechen soll.
- `reasoning_metadata` fuer Signaturen, verschluesselte Reasoning-Inhalte oder separate Reasoning-Token-Metadaten.

Nicht jeder Typ muss sofort in einem ersten Schritt umgesetzt werden. Thinking, Reasoning, Tool Use und Ask User bilden jedoch die erste Prioritaet, waehrend das Part-Modell so erweitert werden soll, dass die weiteren Typen sauber anschliessbar bleiben.

## Arbeitsplan

### 1. Provider- und Modellfaehigkeit pruefen

- Erfassen, welche unterstuetzten Provider und Modelle Reasoning oder Thinking bereits strukturiert liefern.
- Unterscheiden, ob diese Daten als eigene Content-Parts, Stream-Events oder Metadaten wie Reasoning-Tokens ankommen.
- Dokumentieren, welche Anbieter nur Plaintext liefern und welche echte Struktur unterstuetzen.
- Zusaetzlich erfassen, welche Provider strukturierte Refusals, Annotations, Tool-Bloecke, Bild- oder Audioinhalte als eigene Typen liefern.
- Festhalten, wie Tool Use und Human-in-the-loop-Anforderungen im aktiven Nexus-Pfad bereits transportiert werden und wo sie noch textnah oder UI-spezifisch behandelt werden.

### 2. Nexus-Streaming-Pfad analysieren

- Nachvollziehen, an welcher Stelle strukturierte Modellinhalte im aktuellen Streaming-Pfad verloren gehen oder in Text zusammengefuehrt werden.
- Pruefen, welche internen Event- und Content-Typen heute bereits existieren und ob sie fuer Thinking erweitert werden muessen.
- Festlegen, wo die Trennung zwischen `text` und `thinking` beziehungsweise `reasoning` technisch sauber eingefuehrt wird.
- Pruefen, welche weiteren strukturierten Inhalte aktuell verworfen, nur teilweise uebernommen oder nur als `Text` persistiert werden.
- Pruefen, wie `tool_use`, Tool-Ergebnisse und Ask-User-Interaktionen im ersten Release konsistent mit dem erweiterten Part- und Event-Modell abgebildet werden.

### 3. Part-Modell erweitern

- Thinking oder Reasoning als eigenen Part im Stream modellieren.
- Sicherstellen, dass normaler Antworttext weiterhin separat als `type = "text"` transportiert wird.
- Falls verfuegbar, Reasoning-bezogene Token-Metadaten getrennt mitfuehren.
- `tool_use` als strukturierten Assistant-Part oder aequivalenten strukturierten Stream-Typ explizit im Modell verankern.
- Ask-User-Interaktionen als eigenen strukturierten Interaktions-Typ modellieren, statt sie in normalen Assistant-Text oder generische Tool-Resultate zu mischen.
- Das Part-Modell so anlegen, dass kuenftig auch `refusal`, `citation` beziehungsweise `annotation`, `image`, `document`, `audio` und weitere Tool-bezogene Typen ohne Sonderwege aufgenommen werden koennen.
- Zwischen sichtbaren UI-Parts und rein technischen Metadaten unterscheiden, damit Signaturen oder verschluesselte Reasoning-Inhalte nicht versehentlich als normaler Text erscheinen.

### 4. Streaming-Ausgabe anpassen

- Provider-Responses so abbilden, dass strukturierte Thinking-Inhalte nicht in normale Text-Parts gemischt werden.
- Den Stream so ausgeben, dass Continue Thinking direkt als eigenes UI-Element rendern kann.
- Verhindern, dass der finale Assistant-Text den Thinking-Block zusaetzlich sichtbar enthaelt.
- Tool Use im Stream weiterhin strukturiert und getrennt von normalem Antworttext transportieren.
- Ask-User- oder Approval-aehnliche Interaktionen als eigene strukturierte UI-Signale ausgeben, statt sie als normalen Text erscheinen zu lassen.
- Sicherstellen, dass weitere strukturierte Inhalte ebenfalls nicht in einen einzigen `text`-Part kollabieren, wenn der Provider eine saubere Trennung bereits liefert.

### 5. Fallback-Strategie klar abgrenzen

- Kein `<think>...</think>`-Parsing im Continue-Frontend als Hauptloesung vorsehen.
- Falls einzelne Modelle nur Plaintext liefern, diesen Fall als eingeschraenkten Fallback behandeln und nicht als Zielarchitektur.
- Die primaere Loesung bleibt die saubere Strukturierung in Nexus selbst.

### 6. Validierung mit echtem Modell

- Ein Modell auswaehlen, das sichtbares Thinking oder Reasoning tatsaechlich liefert.
- Pruefen, ob Thinking im Stream als eigener Part ankommt.
- Verifizieren, dass Continue Thinking getrennt rendert und der finale Antworttext sauber bleibt.
- Tool Use im selben Validierungspfad mitpruefen, damit Text, Thinking und Tool Use nicht gegeneinander regressieren.
- Einen Ask-User- oder Approval-nahen Interaktionsfall mitpruefen, damit Rueckfragen nicht als normaler Assistant-Text enden.
- Soweit verfuegbar, mindestens einen weiteren strukturierten Inhaltstyp mitpruefen, etwa Refusal, Annotation oder einen providerseitigen Tool-Block.

## Priorisierung der Part-Typen

### Prioritaet 1

- `thinking` oder `reasoning`
- `reasoning_metadata`
- `tool_use`
- `ask_user` beziehungsweise strukturierte Human-interaction-Events

### Prioritaet 2

- `refusal`
- `citation` oder `annotation`

### Prioritaet 3

- `image`
- `document`
- `audio`
- erweiterte Tool- und serverseitige Tool-Ergebnis-Typen

Diese Priorisierung soll verhindern, dass die erste Iteration zu breit wird. Der erste Umsetzungsfokus liegt auf Thinking, Reasoning, Tool Use und Ask User, aber die Architektur soll die spaetere Erweiterung nicht verbauen.

## Rollout-Reihenfolge

Die Umsetzung erfolgt bewusst in klar getrennten Phasen:

### Phase 1: Prioritaet 1 umsetzen

- `thinking` oder `reasoning` end-to-end im Nexus-Streaming einfuehren.
- `reasoning_metadata` aufnehmen, soweit vom Provider verfuegbar.
- `tool_use` im strukturierten Part- und Event-Modell fuer den ersten Release festziehen.
- Ask-User- oder Approval-nahe Interaktionen als strukturierte Signale in denselben ersten Release aufnehmen.
- Persistenz-, Protokoll- und Streaming-Pfade fuer diese erste Ausbaustufe aktualisieren.
- Mit mindestens einem geeigneten Modell validieren.

### Phase 2: Repository aktualisieren und Release erstellen

- Repository nach Abschluss von Prioritaet 1 auf den neuen Stand bringen.
- Dokumentation, Changelog und gegebenenfalls Beispiele fuer die neue Struktur aktualisieren.
- Einen Release mit der abgeschlossenen Prioritaet-1-Unterstuetzung erstellen.

### Phase 3: Uebergabe an das Continue-Team

- Die neue strukturierte Thinking- und Reasoning-Schnittstelle an das Continue-Team kommunizieren.
- Das erwartete Stream-Verhalten, Beispiel-Payloads und eventuelle Metadaten dokumentiert uebergeben.
- Erst nach dieser Uebergabe und einem ersten Integrationsfeedback die naechsten Ausbaustufen angehen.

### Phase 4: Prioritaet 2 und 3 nachziehen

- Erst nach abgeschlossenem Release und Uebergabeprozess `refusal`, `citation` beziehungsweise `annotation` angehen.
- Danach multimodale und weitergehende strukturierte Typen aus Prioritaet 3 umsetzen.

## Externe Referenzpunkte

- Anthropic trennt in der Messages-API bereits zwischen `thinking`, `text`, `tool_use`, `server_tool_use` und strukturierten Tool-Resultaten.
- OpenAI trennt in der Responses-API unter anderem zwischen `reasoning`, `output_text`, `refusal`, Annotationen und verschiedenen Tool- sowie Audio-Events.
- Claude Code verarbeitet in seinem Nachrichtenmodell bereits mehrere Blocktypen getrennt, darunter `text`, `thinking`, `redacted_thinking`, `tool_use`, `tool_result`, `image` und `document`.

Diese Referenzen bestaetigen, dass ein erweitertes Nexus-Part-Modell keine Sonderloesung waere, sondern dem Stand moderner Provider-APIs und Agent-Clients entspricht.

## Akzeptanzkriterien

- Thinking oder Reasoning wird, wenn verfuegbar, als eigener strukturierter Part gestreamt.
- Antworttext wird weiterhin separat als `text` transportiert.
- Der finale Assistant-Text enthaelt keinen zusaetzlich sichtbaren Thinking-Block.
- Continue ist fuer die Hauptloesung nicht auf `<think>`-Parsing angewiesen.
- Mindestens ein unterstuetztes Modell validiert den End-to-End-Pfad erfolgreich.
- Das erweiterte Part-Modell verhindert, dass kuenftig weitere strukturierte Provider-Inhalte zwangsweise in Plaintext kollabieren.
- Persistenz- und Protokollpfade koennen strukturierte Inhalte erhalten, statt sie auf `Role` plus `Text` zu reduzieren.
- Prioritaet 2 und 3 starten erst nach abgeschlossenem Prioritaet-1-Release und Uebergabe an das Continue-Team.
- Der erste Release deckt neben Thinking und Reasoning auch `tool_use` und strukturierte Ask-User-Interaktionen ab.

## Nicht Teil der Loesung

- Kein frontendseitiges `<think>`-Parsing als primaerer Integrationsweg.
- Kein Zusammenfuehren von Thinking und finalem Antworttext in einen einzigen Text-Part, wenn der Provider bereits Struktur liefert.
- Keine Einfuehrung unnoetig vieler UI-Komponenten in der ersten Iteration ohne providerseitigen oder produktseitigen Bedarf.