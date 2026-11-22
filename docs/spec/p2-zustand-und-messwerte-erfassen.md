# P2 – Zustand und Messwerte erfassen

Status: ready-for-agent
Quelle: EventStorming-Session (Process Level) zu P2

## Problem Statement

Ich habe ein Klimagerät zu Hause, sehe aber nirgends, in welchem Zustand es gerade ist (ein/aus, Modus, Solltemperatur, Lüfterstufe …) und welche Messwerte es meldet (Raumtemperatur, Aussentemperatur). Ich kann auch nicht nachvollziehen, wie sich Zustand und Messwerte über die Zeit entwickelt haben. Und weitere geplante Funktionen – Benachrichtigung bei Fehlercode, Einschalten der Klima beim Nachhause-Kommen nur wenn es warm ist – haben heute keine Grundlage, weil niemand feststellt, *dass* sich am Klimagerät etwas geändert hat.

## Solution

Das Backend nimmt jede Minute eine **Messung** des Klimageräts auf: eine Momentaufnahme von Zustand und Messwerten zum Zeitpunkt der Abfrage. Jede Messung landet unverdichtet im **Verlauf**. Der letzte bekannte Stand ist jederzeit als **Aktueller Zustand** abrufbar.

Beim Aufnehmen vergleicht das Klimagerät die neue Messung mit der vorherigen und hält fachlich relevante Änderungen als eigene Domain Events fest (z. B. „Solltemperatur geändert“, „Fehlercode gemeldet“). Nachgelagerte Prozesse (P4 Störung melden, P13 Wohnung einrichten) können später mit eigenen Policies darauf reagieren, ohne dass P2 angepasst werden muss.

### Ablauf (aus dem EventStorming)

```
⏰ jede Minute → [Klimagerät extern] liefert Werte
  → Command: Messung aufnehmen
  → [Aggregate Klimagerät] (kennt die letzte Messung)
      → Messung aufgenommen          (immer)
      → Solltemperatur geändert      (bei Abweichung)
      → Modus geändert               (bei Abweichung)
      → Eingeschaltet / Ausgeschaltet (bei Abweichung)
      → Selbstreinigung gestartet    (bei Abweichung)
      → Fehlercode gemeldet          (Fehlercode ≠ 0 und neu)
      → Messwerte gemeldet           (immer, Raum- und Aussentemperatur)
  → Read Models: Aktueller Zustand, Verlauf
```

P2 enthält keine Policy: Die Regel „jede Minute“ hat nichts zu entscheiden, und das Nachführen der Read Models ist eine Projektion, keine Policy.

## User Stories

1. Als Bewohner möchte ich sehen, ob das Klimagerät ein- oder ausgeschaltet ist, damit ich weiss, ob es gerade läuft.
2. Als Bewohner möchte ich den aktuellen Modus des Klimageräts sehen, damit ich weiss, ob es kühlt, heizt, entfeuchtet oder lüftet.
3. Als Bewohner möchte ich die aktuelle Solltemperatur sehen, damit ich weiss, worauf das Klimagerät regelt.
4. Als Bewohner möchte ich die aktuelle Lüfterstufe und weitere Zustandswerte (z. B. Eco, Swing, Display) sehen, damit ich den vollständigen Zustand kenne.
5. Als Bewohner möchte ich die aktuelle Raumtemperatur sehen, damit ich beurteilen kann, ob es im Raum angenehm ist.
6. Als Bewohner möchte ich die aktuelle Aussentemperatur sehen, damit ich die Leistung des Klimageräts einordnen kann.
7. Als Bewohner möchte ich sehen, zu welchem Zeitpunkt die letzte Messung aufgenommen wurde, damit ich erkenne, wie aktuell die angezeigten Werte sind.
8. Als Bewohner möchte ich einen allfälligen Fehlercode im Aktuellen Zustand sehen, damit ich eine Störung bemerke.
9. Als Bewohner möchte ich den Verlauf von Zustand und Messwerten für einen gewählten Zeitraum abrufen, damit ich nachvollziehen kann, was das Klimagerät wann getan hat.
10. Als Bewohner möchte ich, dass jede Messung unverdichtet im Verlauf landet, damit ich auch kurze Ausschläge sehe.
11. Als Bewohner möchte ich, dass jede Messung den Zeitpunkt der Abfrage trägt (nicht den Zeitpunkt der Verarbeitung), damit der Verlauf auch bei verspäteter Zustellung zeitlich stimmt.
12. Als Bewohner möchte ich, dass Zeiten, in denen keine Messung kam, im Verlauf als echte Lücke erscheinen, damit nichts erfunden wird.
13. Als Bewohner möchte ich, dass der Aktuelle Zustand nach einem Neustart des Backends wieder verfügbar ist, sobald die nächste Messung eintrifft.
14. Als Bewohner möchte ich, dass der Verlauf einen Neustart des Backends überlebt, damit keine Daten verloren gehen.
15. Als künftiger Prozess möchte ich erfahren, wenn sich die Solltemperatur geändert hat, damit ich darauf reagieren kann. (Bisher ohne Konsumenten; P8 ist entfallen.)
16. Als künftiger Prozess möchte ich erfahren, wenn sich der Modus geändert hat, damit ich darauf reagieren kann.
17. Als künftiger Prozess möchte ich erfahren, wenn das Klimagerät ausgeschaltet wurde, damit ich darauf reagieren kann.
18. Als künftiger Prozess möchte ich erfahren, wenn die Selbstreinigung gestartet wurde, damit ich darauf reagieren kann.
19. Als künftiger Prozess möchte ich erfahren, wenn das Klimagerät eingeschaltet wurde, damit ich darauf reagieren kann. (P7 ist entfallen.)
20. Als Prozess „Störung melden“ (P4) möchte ich erfahren, wenn ein neuer Fehlercode gemeldet wird, damit eine Benachrichtigung gesendet werden kann.
21. Als Prozess „Störung melden“ (P4) möchte ich *nicht* jede Minute erneut über denselben, weiterhin anstehenden Fehlercode informiert werden, damit keine Benachrichtigungsflut entsteht.
22. Als nachgelagerter Prozess möchte ich, dass Änderungen an Lüfterstufe, Display, Swing o. ä. kein eigenes Event auslösen, damit ich nur auf fachlich relevante Änderungen reagiere.
23. Als nachgelagerter Prozess möchte ich, dass Änderungen unabhängig von ihrer Quelle (Fernbedienung, UI, Backend-Befehl) gemeldet werden, damit die Bewertung der Quelle bei mir liegt und nicht in P2.
24. Als nachgelagerter Prozess möchte ich, dass bei der ersten Messung nach dem Start keine Änderungs-Events entstehen, weil es keinen Vergleichswert gibt.
25. Als nachgelagerter Prozess möchte ich, dass eine Messung nach einer Lücke mit der letzten bekannten Messung verglichen wird, damit Änderungen während einer Offline-Phase nicht verloren gehen.
26. Als Bewohner möchte ich, dass eine doppelt zugestellte Messung (gleicher Zeitpunkt der Abfrage) nur einmal im Verlauf erscheint und keine Events doppelt auslöst.
27. Als Entwickler möchte ich, dass das Backend nur eine Messung kennt und nicht weiss, wie sie technisch hereinkommt, damit der Hilfsdienst bzw. MQTT austauschbar bleibt (ADR 0001).
28. Als Entwickler möchte ich, dass neue Prozesse sich an die Events des Klimageräts anhängen können, ohne P2 zu ändern.
29. Als Prozess „Wohnung einrichten“ (P13) möchte ich zu jeder aufgenommenen Messung Raumtemperatur, Aussentemperatur und den Zeitpunkt der Abfrage erfahren, damit ich beim Nachhause-Kommen prüfen kann, ob es warm ist.

## Implementation Decisions

- **Aggregate Klimagerät** ist der einzige Entscheidungspunkt in P2. Es nimmt den Command *Messung aufnehmen* entgegen, kennt die letzte Messung, vergleicht und erzeugt in einem Schritt *Messung aufgenommen* sowie die abgeleiteten Events. Variante „Policy wertet nachgelagert aus“ wurde verworfen (längere Kette, Gefahr veralteter Vergleichsbasis, kein fachlicher Gewinn).
- **Ein Snapshot-Event pro Messung**, kein Event pro Einzelwert (kein Property-Changed-Anti-Pattern). Eigene Events gibt es nur dort, wo ein Prozess genau darauf reagiert:
  - Fehlercode gemeldet → P4
  - Messwerte gemeldet → P13 (Warm-Prüfung)
  - Solltemperatur geändert, Modus geändert, Eingeschaltet, Ausgeschaltet, Selbstreinigung gestartet → heute ohne Konsumenten (P7 und P8 sind entfallen); sie bleiben als fachlich relevante Änderungen veröffentlicht
- **Fehlercode gemeldet** entsteht, wenn der Fehlercode ≠ 0 ist und sich vom Fehlercode der vorherigen Messung unterscheidet. Bei der ersten Messung nach dem Start entsteht es, wenn der Fehlercode ≠ 0 ist (Ausnahme zur Regel „erste Messung erzeugt keine Änderungs-Events“, damit eine anstehende Störung nicht unbemerkt bleibt).
- **Erste Messung** nach dem Start setzt nur die Vergleichsbasis; sie erzeugt *Messung aufgenommen*, aber keine Änderungs-Events (ausser Fehlercode, s. o.).
- **Vergleich nach einer Lücke** erfolgt gegen die letzte bekannte Messung, egal wie alt.
- **Zeitstempel** einer Messung ist der Zeitpunkt der Abfrage beim Klimagerät; er kommt mit der eingehenden Messung mit.
- **Duplikate und Reihenfolge**: Eine Messung mit gleichem oder älterem Zeitpunkt der Abfrage als die letzte bekannte wird verworfen (kein Verlaufseintrag, keine Events).
- **Eingangsschnittstelle Messung**: Das Backend definiert eine fachliche Eingangsschnittstelle „Messung empfangen“, die eine vollständige Messung (Zeitpunkt der Abfrage, Zustand, Messwerte, Fehlercode) entgegennimmt. Der MQTT-Adapter gemäss ADR 0001 ist nur eine Implementierung davon; er ist nicht Teil dieser Spec (s. Out of Scope).
- **Veröffentlichung für andere Module** (Architecture Spine AD-8, AD-17): Die sechs abgeleiteten Events werden 1:1 als Integration Events in `AirConditioner.Contracts` über die Outbox veröffentlicht, sodass spätere Prozesse (P4, P13) Policies anhängen können, ohne das Aggregate zu kennen. Zusätzlich wird mit jeder aufgenommenen Messung `ReadingsReported` (Raumtemperatur, Aussentemperatur, Zeitpunkt der Abfrage) veröffentlicht; Home hält daraus den letzten Wert. Ob pro Messung oder nur bei Änderung, ist offen (siehe Further Notes). `MeasurementRecorded` bleibt modulintern.
- **Read Model Aktueller Zustand**: letzte Messung des Klimageräts inkl. Zeitpunkt der Abfrage; per HTTP abrufbar.
- **Read Model Verlauf**: alle Messungen, unverdichtet, nach Zeitpunkt der Abfrage geordnet; per HTTP für einen Zeitraum (von/bis) abrufbar.
- **Persistenz**: Der Verlauf und die letzte Messung werden in PostgreSQL gespeichert (ADR 0002), angebunden über EF Core und hinter einer Schnittstelle gekapselt. Sie überleben einen Neustart. Eine normale Tabelle mit Index auf dem Zeitpunkt der Abfrage genügt; das Schema wird über EF-Core-Migrationen angelegt.
- **Benennung im Code** gemäss ADR 0005: Der Code ist englisch, die Code-Namen stehen in CONTEXT.md (z. B. `AirConditioner`, `RecordMeasurement`, `MeasurementRecorded`, `TargetTemperatureChanged`).
- **Fähigkeiten** sind nicht Teil der minütlichen Messung; sie gehören zu P1.
- Das Projekt-Template (Beispiel-Endpoint „weatherforecast“) wird entfernt.

## Testing Decisions

- **Ein Seam: die Grenze des Backends.** Tests starten das Backend in-process (WebApplicationFactory), speisen Messungen über einen Fake der Eingangsschnittstelle „Messung empfangen“ ein und prüfen ausschliesslich beobachtbares Verhalten:
  - die Read Models *Aktueller Zustand* und *Verlauf* über HTTP,
  - die veröffentlichten Integration Events über einen im Test registrierten Konsumenten (Zustellung durch den Outbox-Worker).
- Das Aggregate Klimagerät wird **nicht** separat getestet; es ist ein Implementierungsdetail hinter dem Seam.
- Gute Tests beschreiben Szenarien in Fachsprache, z. B. „Wenn zwei Messungen mit unterschiedlicher Solltemperatur eintreffen, dann wird *Solltemperatur geändert* veröffentlicht“, und prüfen keine internen Klassen oder Speicherdetails.
- Abzudecken sind mindestens: `ReadingsReported` zu jeder aufgenommenen Messung (nicht bei Duplikat oder älterer Messung); erste Messung; unveränderte Messung; je eine Änderung pro abgeleitetem Event; Änderung nur an Lüfterstufe/Display (kein Event); neuer vs. weiterhin anstehender Fehlercode; Fehlercode bei erster Messung; Duplikat und ältere Messung; Lücke im Verlauf; Verlauf nach Zeitraum; Aktueller Zustand vor der ersten Messung (leer).
- Die Tests laufen gegen ein echtes PostgreSQL via Testcontainers (Docker nötig, lokal und in der CI). Neustart-Tests starten das Backend neu gegen dieselbe Datenbank.
- **Prior Art**: keine; dies sind die ersten Tests im Repo. Das Testprojekt wird mit diesem Slice angelegt.

## Out of Scope

- MQTT-Adapter und Python-Hilfsdienst (eigener Slice; Test mit echtem Broker, z. B. Testcontainers, folgt dort).
- Erkennen von *Offline* und *wieder online* sowie Benachrichtigungen (P3).
- Benachrichtigung bei Fehlercode (P4) – P2 liefert nur das Event.
- Unterscheidung der Quelle einer Änderung (Fernbedienung vs. Backend-Befehl); ohne Zeitsteuerung gibt es dafür keinen Prozess mehr.
- Erfragen der Fähigkeiten (P1).
- Event „Fehlercode behoben“ (bei Bedarf in P4 klären).
- UI; diese Spec liefert nur die HTTP-Schnittstelle der Read Models.
- Verdichtung, Aufbewahrungsdauer oder Löschung alter Verlaufsdaten.

## Further Notes

- **Bestätigt** (Q3a–Q8): Event-Name „Messung aufgenommen“; Liste der abgeleiteten Events inkl. „Eingeschaltet“; Verlauf = jede Messung unverdichtet, Offline-Phasen als echte Lücken; Zeitpunkt der Abfrage beim Klimagerät als Zeitstempel; antwortet das Klimagerät nicht, entsteht keine Messung (kein eigenes Signal); Fähigkeiten nur in P1; Read Models Aktueller Zustand und Verlauf.
- **CONTEXT.md nachführen**: neuen Begriff *Messung* aufnehmen („Momentaufnahme von Zustand und Messwerten eines Klimageräts zum Zeitpunkt der Abfrage“); bei *Verlauf* „lückenlos“ ersetzen durch „jede Messung, unverdichtet“.
- **Speichertechnologie**: entschieden in ADR 0002 (PostgreSQL).
- **Hotspots für spätere Prozesse**:
  - P3: Ist „Klimagerät offline“ ein Event des Aggregates Klimagerät, obwohl es aus dem Ausbleiben einer Messung entsteht?
  - P3: Soll ein Ausfall des Hilfsdienstes bzw. von MQTT von einem nicht erreichbaren Klimagerät unterschieden werden (z. B. über ein Signal „Abfrage fehlgeschlagen“)? In P2 sehen beide gleich aus: keine Messung.
  - P13: Wird `ReadingsReported` bei jeder Messung (1/min) oder nur bei Änderung veröffentlicht? Wie stösst „aktuellen Status holen“ eine frische Messung an?
- **Aktualisiert 2026-11-21**: Die Zeitsteuerung (P6–P8) ist entfallen; Konsumenten P7/P8 sind entfernt und `ReadingsReported` für P13 ist neu (Architecture Spine AD-17). Die Hotspots zu P7/P8 sind hinfällig.
