# Fachsprache im Code: Fachliches auf Deutsch, Technisches auf Englisch

Was in CONTEXT.md als Begriff steht, heisst im Code genauso, auf Deutsch. Alles andere ist Englisch: technische Bausteine, Frameworks und Muster (Repository, Controller, DbContext, Handler, Options). Zusammengesetzte Namen verbinden beides, z. B. `MessungRepository`, `KlimageraetController`.

Beispiele:

| Fachlich (CONTEXT.md) | Im Code |
|---|---|
| Klimagerät (Aggregate) | `Klimageraet` |
| Command „Messung aufnehmen“ | `MessungAufnehmen` |
| Event „Messung aufgenommen“ | `MessungAufgenommen` |
| Event „Solltemperatur geändert“ | `SolltemperaturGeaendert` |
| Übersteuerung | `Uebersteuerung` |

Umlaute werden überall ersetzt, auch im C#-Code, in URLs, Datenbanknamen, JSON und MQTT-Topics: ä → ae, ö → oe, ü → ue (Ä → Ae, Ö → Oe, Ü → Ue). Ein ß kommt wegen der Schweizer Schreibweise nicht vor. So gilt eine einzige Regel, und Tools, Terminals und Tastaturen machen keine Probleme.

Der Grund: CONTEXT.md, Specs, EventStorming und Code sollen dieselbe Sprache sprechen. Eine Übersetzungsschicht ins Englische würde auseinanderlaufen (heisst *Übersteuerung* `Override` oder `ManualIntervention`?) und Agents zum Mischen verleiten.

## Considered Options

- **Alles Englisch, Übersetzungstabelle in CONTEXT.md**: verworfen. Zwei Sprachen für dieselben Begriffe driften auseinander.
- **Alles Deutsch, auch Technisches**: verworfen. Übersetzte Musterbegriffe (z. B. „Ablage“ statt Repository) wirken hölzern und erschweren die Arbeit mit Framework-Dokumentation.
- **Umlaute im C#-Code beibehalten**: verworfen. Ein Bezeichner wäre dann je nach Ort (Code oder URL/Datenbank) unterschiedlich geschrieben. Eine einheitliche Ersetzungsregel ist einfacher.

## Consequences

- Wer im Code nach einem Fachbegriff sucht, ersetzt die Umlaute (Suche nach `Klimageraet`, nicht nach „Klimagerät“).
- Neue Fachbegriffe kommen zuerst in CONTEXT.md und erst dann in den Code.
