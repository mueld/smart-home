# Code auf Englisch, Fachsprache in Dokumenten auf Deutsch

Status: vorgeschlagen. Löst ADR 0003 ab.

Der gesamte Code ist englisch: Typen, Methoden, Variablen, Module, Namespaces, Tabellen und Spalten, URLs, JSON-Felder, MQTT-Topics, Konfigurationsschlüssel, Testnamen, Commit-Messages und Code-Kommentare. Dokumente (CONTEXT.md, SPEC, Specs, ADRs, Architecture Spine) bleiben deutsch.

Damit Code und Dokumente dieselben Begriffe meinen, trägt jeder Begriff in CONTEXT.md genau einen englischen Code-Namen (Zeile `_Code_:`). CONTEXT.md ist die einzige Übersetzungstabelle; ein Begriff ohne Code-Namen darf nicht im Code vorkommen. Neue Begriffe kommen zuerst mit Code-Namen in CONTEXT.md und erst dann in den Code.

Beispiele:

| Fachlich (CONTEXT.md) | Im Code |
|---|---|
| Klimagerät (Aggregate) | `AirConditioner` |
| Command „Messung aufnehmen“ | `RecordMeasurement` |
| Event „Messung aufgenommen“ | `MeasurementRecorded` |
| Event „Solltemperatur geändert“ | `TargetTemperatureChanged` |
| Anwesenheit (Aggregate) | `Presence` |
| Modul Zuhause | `Home` |

## Considered Options

- **Deutsche Fachbegriffe im Code (ADR 0003)**: abgelöst. Der Bewohner will englischen Code; Framework-Dokumentation, Bibliotheken und Agents arbeiten durchgehend englisch, und Umlaut-Ersetzungen (`Klimageraet`) entfallen.
- **Alles auch in Dokumenten englisch**: verworfen. Das Projekt ist deutschsprachig (Specs, CONTEXT.md, EventStorming); nur der Code wechselt.

## Consequences

- Das Risiko aus ADR 0003 bleibt: Zwei Sprachen für dieselben Begriffe können auseinanderlaufen. Dagegen gilt die Regel oben: ein Code-Name pro Begriff, nur in CONTEXT.md gepflegt.
- Specs, Spine und Stories nennen Bezeichner weiter in der Sprache des Dokuments; wo sie Code-Namen nennen, sind es die englischen aus CONTEXT.md. Die Umstellung der bestehenden Dokumente ist ein eigener Schritt.
- Die Umlaut-Regel (ae/oe/ue) entfällt im Code. In deutschen Dokumenten gelten normale Umlaute.
- Bestehender Code (derzeit `Outcome` im Kernel und das Testprojekt) ist bereits englisch; es muss nichts umbenannt werden.
- Wer im Code nach einem Begriff sucht, schlägt den Code-Namen in CONTEXT.md nach.
