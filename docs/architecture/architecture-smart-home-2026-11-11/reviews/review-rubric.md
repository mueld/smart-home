---
review: rubric-walker
target: ARCHITECTURE-SPINE.md (status draft, 2026-11-11)
reviewer: unabhaengiger Rubric Walker
date: 2026-11-11
sources:
  - ../ARCHITECTURE-SPINE.md
  - ../.memlog.md
  - ../../../../CONTEXT.md
  - ../../../adr/0001-python-hilfsdienst-mit-mqtt.md
  - ../../../adr/0002-verlauf-in-postgresql.md
  - ../../../adr/0003-fachsprache-im-code.md
  - ../../../spec/p2-zustand-und-messwerte-erfassen.md
---

# Review Architecture Spine smart-home – Rubric Walk

## Gesamturteil

Die Spine ist für einen Entwurf erstaunlich dicht: Modulschnitt, Abhängigkeitsrichtung, Datenhoheit, Command-Ablauf, Outbox, Fehler- und Zeitmodell sind klar und grösstenteils durchsetzbar formuliert. **Für den ersten Slice (P2) ist sie aber noch nicht baureif:** Sie widerspricht der Spec P2 bei der Frage, *welche* Events nachgelagerte Prozesse sehen (Domain Events vs. Integration Events), lässt die Schnittstelle zum zweiten Baustein (Python-Hilfsdienst über MQTT) offen, regelt weder Nebenläufigkeit noch Idempotenz der Messung und hat in AD-14 eine Lücke beim Fehlerfall eines beigetretenen Commands. Mehrere Dimensionen (Migrationen anwenden, Umgebungen, Secrets, Architektur-Tests) sind weder entschieden noch deferred noch als offene Frage markiert.

Befunde: 1 critical, 5 high, 9 medium, 6 low.

## Checkliste im Überblick

| Kriterium | Ergebnis |
| --- | --- |
| Echte Divergenzpunkte fixiert, keiner verpasst | Teilweise. Verpasst: Event-Sichtbarkeit Domain/Integration (K-1), MQTT-Vertrag (H-1), Nebenläufigkeit (H-2), Nicht-HTTP-Einstiegspunkte (H-3), Persistenzzugriff (M-1), Read Models (M-2), Aggregate-Identität (M-3), Konsumenten-Form (H-5), Reihenfolge (M-4), HTTP-Konventionen (M-7). |
| AD-Rules durchsetzbar, Prevents wirksam | Meist ja. AD-14 verhindert „halb ausgeführte Commands“ nicht vollständig (H-4). AD-4, AD-6, AD-11, AD-14 haben keinen Durchsetzungsmechanismus (M-6). |
| Deferred unschädlich | Grösstenteils. „Retry/Backoff zentral“ verschweigt Reihenfolge und Poison-Messages, die Konsumenten betreffen (M-4); Serialisierung der Outbox ist faktisch nicht deferrable (L-3). |
| Widersprüche zu ADRs, CONTEXT.md, Spec P2 | Spec P2: Domain Events für P4/P7/P8 (K-1), Test-Event-Empfänger (K-1), Eingangsschnittstelle (H-3). ADR 0002: „hinter einer Schnittstelle gekapselt“ (M-1). CONTEXT.md: keine Widersprüche gefunden. |
| Abdeckung Spec P2 | Test-Seam: ja, aber Event-Empfänger und Outbox-Determinismus offen (K-1, M-8). Read Models: nur indirekt über AD-4 (M-2). Eingangsschnittstelle: nur „wird zum Command“ (H-3). Duplikate/Reihenfolge: nicht abgesichert (H-2). |
| Dimensionen entschieden/deferred/offen | Lücken bei Migrationen anwenden (M-5), Umgebungen und Deployment (M-9), Secrets und Broker-Sicherheit (M-9), Architektur-Tests (M-6). |
| Widersprüche zwischen ADs | AD-3 vs. AD-4/CQRS (H-5 bzw. M-10), AD-4 vs. AD-9 (H-3), AD-6-Ablauf vs. AD-14 (L-2), AD-11 vs. AD-12 (L-4). AD-6/AD-7/AD-8 sind untereinander konsistent. |

---

## Critical

### K-1 — Welche Events sehen P4/P7/P8 und der Test? Widerspruch zur Spec P2

- **Stelle:** AD-6, AD-7, AD-8, Capability-Map P2; Spec P2 „Implementation Decisions“ (*Domain Events werden in-process veröffentlicht, sodass spätere Prozesse (P4, P7, P8) Policies anhängen können*), User Story 28, „Testing Decisions“ (*die veröffentlichten Domain Events über einen im Test registrierten Event-Empfänger*).
- **Problem:** Die Spec P2 lässt P4, P7 und P8 direkt an Domain Events hängen. Laut AD-1 liegen P4 (Benachrichtigung) und P7/P8 (Steuerung) aber in anderen Modulen, und AD-6/AD-7 verbieten genau das: Fremde Module reagieren nur auf Integration Events aus `Klimageraet.Contracts`. Die Spine sagt nirgends,
  1. ob P2 die sechs abgeleiteten Events (`SolltemperaturGeaendert`, `ModusGeaendert`, `Eingeschaltet`, `Ausgeschaltet`, `SelbstreinigungGestartet`, `FehlercodeGemeldet`) **schon jetzt** als Integration Events veröffentlicht. Wenn nicht, muss P2 bei P4/P7/P8 geändert werden, was User Story 28 verletzt.
  2. wer Domain Events auf Integration Events abbildet (ein Domain-Event-Handler im Klimageraet-Modul, der in die Outbox schreibt? 1:1 oder selektiv?).
  3. wie Domain- und Integration Event mit demselben Fachnamen heissen (ADR 0003 kennt nur `MessungAufgenommen`; zwei Typen gleichen Namens in `Domain/` und `Contracts/` führen zu Verwechslungen).
  4. was der im Test registrierte „Event-Empfänger“ abonniert. Ein Domain-Event-Handler im Test sieht Events vor dem Commit (auch bei Rollback); ein Integration-Event-Konsument sieht sie erst nach asynchroner Outbox-Zustellung.

  Der P2-Slice wird damit auf mindestens zwei Arten gebaut werden, die sich gegenseitig ausschliessen.
- **Fix:** Neue AD „Öffentliche Events des Klimageraets“:
  - Jedes fachliche Domain Event, das ein Prozess eines anderen Moduls braucht, hat ab seinem Entstehungs-Slice ein gleichnamiges Integration Event in `<Modul>.Contracts`. Für P2 sind das alle sechs abgeleiteten Events plus `MessungAufgenommen`.
  - Die Abbildung erfolgt in genau einem Domain-Event-Handler pro Event im eigenen Modul, der in die Outbox schreibt (AD-8).
  - Namenskonvention: Domain Event `SolltemperaturGeaendert` in `Domain/`, Integration Event im Namespace `Klimageraet.Contracts` gleichnamig oder mit technischem Suffix `…IntegrationEvent` (ADR 0003 erlaubt englische technische Suffixe). Entscheiden und in „Consistency Conventions“ eintragen.
  - Test-Seam: Der Test-Event-Empfänger ist ein Integration-Event-Konsument. Der Test stellt die Outbox deterministisch zu (siehe M-8).
  - Spec P2 nachführen („Domain Events werden in-process veröffentlicht“ → „…und als Integration Events über die Outbox veröffentlicht“).

---

## High

### H-1 — Vertrag zwischen Backend und Python-Hilfsdienst (MQTT) fehlt

- **Stelle:** AD-9, Structural Seed (Deployment-Diagramm), Stack; ADR 0001; ADR 0003 (Umlaut-Regel gilt auch für MQTT-Topics).
- **Problem:** Der Python-Hilfsdienst ist eine eigenständig gebaute Unit. Ihre Schnittstelle zum Backend ist der klassische Divergenzpunkt, wird aber nirgends fixiert: Topic-Schema, Payload-Format (JSON-Felder, Einheiten, Enum-Werte für Modus/Lüfterstufe), Format des Zeitpunkts der Abfrage (UTC, ISO 8601?), QoS-Level (bei QoS 1 sind Duplikate garantiert, siehe H-2), Retained Messages, Request/Reply für Befehle und Fähigkeiten (P1), Geräte-Identifikation im Topic, Schema-Version. Die Spec P2 klammert den MQTT-Adapter aus, die Spine bindet ihn aber (Scope „inkl. Anbindung von … MQTT“).
- **Fix:** AD „MQTT-Vertrag“ mit: Topic-Schema (z. B. `smarthome/klimageraet/{geraeteId}/messung`, `…/befehl`, `…/faehigkeiten`, Umlaute ersetzt), JSON-Payload mit `schemaVersion`, `zeitpunktDerAbfrage` als UTC-ISO-8601, QoS 1 und „Konsument verträgt Duplikate“, kein Retain für Messungen, Request/Reply über Correlation-Id. Das konkrete Payload-Schema kann als Datei neben der Spine liegen (z. B. `contracts/mqtt.md`) und ist für beide Seiten verbindlich. Wenn die Festlegung bewusst später erfolgen soll: explizit unter Deferred mit Begründung, warum bis zum MQTT-Slice keine Divergenz entsteht.

### H-2 — Nebenläufigkeit und Idempotenz von `MessungAufnehmen` nicht abgesichert

- **Stelle:** AD-4, AD-6, AD-8, AD-12; Spec P2 User Story 26 und „Duplikate und Reihenfolge“.
- **Problem:** Die Spec verlangt, dass eine doppelte oder ältere Messung verworfen wird. Das Aggregate prüft das gegen „die letzte Messung“. Laufen zwei Messungen gleichzeitig (MQTT QoS 1 liefert Duplikate; der Konsument kann parallel verarbeiten; ein Zeit-Trigger aus P3 schreibt evtl. gleichzeitig dasselbe Aggregate), lesen beide denselben Stand, und beide werden gespeichert, inklusive doppelter Events in der Outbox. Die Spine legt weder Optimistic Concurrency (z. B. `xmin`) noch einen Unique-Index noch eine serielle Verarbeitung fest. Ohne Regel löst jeder Slice das anders oder gar nicht.
- **Fix:** AD „Nebenläufigkeit“: Jedes Aggregate hat ein Concurrency Token (PostgreSQL `xmin` über Npgsql). `DbUpdateConcurrencyException` ist eine Exception (AD-10), der Einstiegspunkt wiederholt den Command einmal bzw. die Outbox stellt erneut zu. Zusätzlich für P2 ein Unique-Index auf `(klimageraet_id, zeitpunkt_der_abfrage)` als zweite Sicherung. Ausserdem festlegen: Eine verworfene Messung ist ein **erfolgreiches** Result (No-op), kein `ValidationError`, damit der MQTT-Konsument nicht bei jedem Duplikat einen Fehler loggt.

### H-3 — Nicht-HTTP-Einstiegspunkte und die Eingangsschnittstelle „Messung empfangen“

- **Stelle:** AD-4 („Jedes Feature … mit Endpoint“), AD-9 („Eingehende Messungen werden zum Command `MessungAufnehmen`“), AD-14 (nennt „Zeit-Trigger“ und „Outbox-Konsument“), Tests-Konvention; Spec P2 „Eingangsschnittstelle Messung“ und „Testing Decisions“ (Fake der Eingangsschnittstelle).
- **Problem:**
  1. AD-4 verlangt einen Endpoint pro Feature. `MessungAufnehmen` (P2), die Offline-Erkennung (P3) und Zeitplan ausführen (P7) haben keinen HTTP-Endpoint. AD-4 und AD-9 widersprechen sich hier formal.
  2. Die Spec definiert eine fachliche Eingangsschnittstelle „Messung empfangen“, die im Test gefakt wird. Die Spine sagt nicht, ob das ein eigenes Interface ist (z. B. `IMessungEmpfang`, das der MQTT-Adapter aufruft) oder ob der Adapter direkt `ISender.Send(new MessungAufnehmen(...))` aufruft. Was der Test fakt, bleibt unklar: Bei Variante 2 gibt es nichts zu faken, der Test schickt den Command.
  3. Für Hintergrund-Einstiegspunkte (MQTT-Nachricht, Zeit-Trigger, Outbox-Konsument) fehlt die Regel „pro Nachricht bzw. Tick ein eigener DI-Scope, Aufruf nur über den Mediator, Span-Start, Umgang mit Fehler-Results (loggen statt ProblemDetails)“. AD-10 bildet Results nur auf HTTP ab.
  4. Der Mechanismus für Zeit-Trigger (pro Modul ein `BackgroundService` mit `PeriodicTimer` auf `TimeProvider`? zentraler Scheduler im Kernel?) ist nicht festgelegt; P3 und P7 werden ihn unabhängig voneinander erfinden.
- **Fix:** AD „Einstiegspunkte“: Ein Feature hat genau einen Einstiegspunkt: HTTP-Endpoint, MQTT-Konsument, Zeit-Trigger oder Integration-Event-Konsument. Alle Nicht-HTTP-Einstiegspunkte liegen im Feature-Ordner, erzeugen pro Auslösung einen DI-Scope, senden über den Mediator und loggen Fehler-Results strukturiert. Zeit-Trigger laufen über eine Kernel-Basisklasse auf `TimeProvider`. Für P2 festlegen: „Messung empfangen“ = Interface im Modul Klimageraet, implementiert vom Feature `MessungAufnehmen`. Der MQTT-Adapter ruft es auf, der Test ruft es direkt auf (oder über einen Test-Treiber). AD-4 entsprechend auf „Einstiegspunkt“ statt „Endpoint“ umformulieren.

### H-4 — AD-14: Fehler-Result eines beigetretenen Commands kann teilweise committet werden

- **Stelle:** AD-14 („Nur das äusserste Transaction Behavior committet … oder rollt zurück (bei Fehler-Result oder Exception); ein beigetretenes committet nie selbst.“)
- **Problem:** Der innere Command (z. B. `SolltemperaturSetzen` in Klimageraet) kann `SaveChanges` aufrufen und danach ein Fehler-Result liefern. Ebenso kann seine Validation fehlschlagen, nachdem der äussere Handler schon geschrieben hat. Der beigetretene Behavior darf nicht zurückrollen. Fängt der äussere Handler das Fehler-Result ab und liefert selbst Erfolg (fachlich plausibel: „Befehl nicht möglich, trotzdem Zeitplan speichern“), committet das äusserste Behavior die halben Änderungen des inneren Moduls inklusive seiner Outbox-Einträge (etwa ein MQTT-Befehl). Genau das soll AD-14 laut „Prevents“ verhindern.
- **Fix:** Regel ergänzen: Ein beigetretenes Transaction Behavior legt vor dem Handler einen Savepoint an (`Database.CreateSavepointAsync`, von Npgsql unterstützt) und rollt bei Fehler-Result oder Exception auf diesen Savepoint zurück. Danach leert es den Change Tracker des betroffenen DbContexts. Alternative: Ein Fehler-Result eines beigetretenen Commands markiert die Transaktion als rollback-only, und das äusserste Behavior rollt dann zwingend zurück. Eine der beiden Varianten wählen und im Sequenzdiagramm zeigen. Zusätzlich festhalten: kein `EnableRetryOnFailure` (Execution Strategy) auf den DbContexts, weil benutzerinitiierte Transaktionen damit nicht kompatibel sind. Das passt zu AD-10 (transiente Fehler sind Exceptions).

### H-5 — Form und Idempotenz der Integration-Event-Konsumenten offen

- **Stelle:** AD-5 (Pipeline nur für Commands), AD-8 („über den Mediator an die Handler anderer Module, die in einer eigenen Transaktion laufen“, „jeder Konsument verträgt Duplikate“), Deferred „Retry, Backoff“.
- **Problem:**
  1. Ein Integration-Event-Handler ist weder Command noch Query. Unklar ist, ob er durch die Pipeline läuft und wie das Transaction Behavior den DbContext findet, das laut AD-5 „anhand des Commands“ auflöst.
  2. Bei mehreren Konsumenten eines Events (z. B. `FehlercodeGemeldet` → Benachrichtigung und später Steuerung) ist offen, ob die Zustellung pro Konsument getrackt wird. Wird nur pro Outbox-Eintrag getrackt, führt der Fehler eines Konsumenten zur erneuten Zustellung an alle. Das bläht die Duplikate auf und koppelt Module.
  3. „Verträgt Duplikate“ ist ohne Mechanismus nicht durchsetzbar. Laut Memlog ist die Inbox-Tabelle bewusst verworfen. Dann muss aber gesagt werden, *wie* ein Konsument idempotent wird (natürlicher Schlüssel, Event-Id im Ziel-Aggregate, „doppelte Telegram-Nachricht akzeptiert“).
- **Fix:** AD-8 ergänzen: Ein Integration-Event-Konsument ist ein Feature im konsumierenden Modul. Er übersetzt das Event in einen eigenen Command des Moduls, der durch die volle Pipeline läuft. Damit gelten Transaktion, Validation und Modul-Zuordnung unverändert. Die Zustellung wird pro (Outbox-Eintrag, Konsument) getrackt. Jeder Konsument dokumentiert seine Idempotenzstrategie in einer von drei erlaubten Formen: fachlich idempotent, Event-Id am Aggregate gespeichert, Duplikat fachlich akzeptiert.

---

## Medium

### M-1 — Persistenzzugriff: Repository oder DbContext direkt? (ADR 0002)

- **Stelle:** AD-4, Ebenen-Tabelle; ADR 0002 („Der Zugriff ist hinter einer Schnittstelle gekapselt, sodass die Fachlogik PostgreSQL nicht kennt“); Spec P2 „Persistenz … hinter einer Schnittstelle gekapselt“; ADR 0003 Beispiel `MessungRepository`.
- **Problem:** Die Spine sagt nicht, ob Command-Handler den Modul-DbContext direkt nutzen (üblich in VSA) oder über ein Repository pro Aggregate gehen. Queries nutzen laut AD-4 „Projektion“, also vermutlich den DbContext. Ohne Regel macht der erste Slice das eine, der zweite das andere. Liest man ADR 0002 wörtlich, verlangt er eine Schnittstelle.
- **Fix:** AD ergänzen: „Command-Handler laden und speichern Aggregates über ein Repository pro Aggregate (`KlimageraetRepository`); Query-Handler lesen direkt über den Modul-DbContext (`AsNoTracking`, Projektion). Die Domäne kennt weder EF Core noch Npgsql.“ Oder bewusst „DbContext direkt in Handlern“ festlegen und ADR 0002 präzisieren („Fachlogik“ = Domain-Schicht, nicht Handler).

### M-2 — Read Models „Aktueller Zustand“ und „Verlauf“ nicht verankert; Aggregate-Grösse

- **Stelle:** AD-4 („Queries lesen direkt per Projektion“); Spec P2 Read Models; ADR 0002 („Verlauf und letzte Messung pro Klimagerät“, rund 525'000 Zeilen pro Jahr).
- **Problem:** Unklar ist, ob Read Models eigene Tabellen sind, die ein Domain-Event-Handler synchron nachführt, oder Abfragen auf die Schreibtabellen. Ebenso unklar ist, wie das Aggregate „die letzte Messung kennt“, ohne den ganzen Verlauf zu laden: Messung als Kind-Collection des Aggregates vs. eigene Tabelle mit „letzte Messung“ als Wert am Aggregate. Für den Verlauf fehlen ausserdem eine Begrenzung oder Paging. Eine Anfrage über ein Jahr liefert rund 525'000 Zeilen auf dem Pi. Das betrifft auch spätere Read Models (P6 Zeitplan-Übersicht).
- **Fix:** In AD-4 bzw. einer neuen AD festhalten: (a) Das Aggregate hält nur die letzte Messung als Wert. Der Verlauf ist eine eigene Append-only-Tabelle, die der Command in derselben Transaktion schreibt; er wird nie über das Aggregate geladen. (b) Read Models sind Abfragen auf Modul-Tabellen, eigene Projektionstabellen nur mit Begründung. Falls doch, führt sie ein Domain-Event-Handler synchron nach (AD-6). (c) Zeitraum-Queries haben eine maximale Spanne oder Paging, und Queries werden ebenfalls validiert (von < bis, Pflichtfelder). Dafür braucht es entweder ein Validation Behavior auch für Queries oder eine Regel „Query-Parameter werden im Endpoint validiert“.

### M-3 — Identität und Entstehung des Aggregates Klimageraet

- **Stelle:** AD-12 (GUID v7, im Code erzeugt), AD-9, H-1.
- **Problem:** Die eingehende Messung trägt eine Geräte-Kennung des Hilfsdienstes (Midea-Device-Id o. ä.), das Aggregate eine GUID v7. Offen ist, wer das Aggregate wann anlegt (erste Messung? P1 Fähigkeiten? Seed?) und wie die externe Kennung auf die GUID abgebildet wird. Spec P2 und P1 werden das unterschiedlich lösen. Auch die HTTP-Routen der Read Models hängen davon ab (`/klimageraete/{id}/…` mit welcher Id?).
- **Fix:** Festlegen: Das Aggregate trägt zusätzlich die externe `GeraeteKennung` (unique). Die erste Messung oder P1 (eines von beiden wählen) legt es an. Routen und Contracts verwenden ausschliesslich die GUID. Neuer Begriff ggf. zuerst in CONTEXT.md (ADR 0003).

### M-4 — Reihenfolge der Outbox-Zustellung und Poison Messages fälschlich als „keine Divergenz“ deferred

- **Stelle:** Deferred „Retry-, Backoff- und Aufräumregeln der Outbox … darum keine Divergenz zwischen Slices“.
- **Problem:** Der Mechanismus liegt zentral, seine **Garantien** bestimmen aber das Design der Konsumenten. P8 muss wissen, ob `Ausgeschaltet` vor `Eingeschaltet` ankommt, oder ob es mit Reihenfolgevertauschung rechnen muss (etwa über den Zeitpunkt der Abfrage im Event). Ebenso muss jeder Konsument wissen, ob ein dauerhaft fehlschlagendes Event die Zustellung nachfolgender blockiert. Wird das erst „mit dem ersten Worker“ festgelegt, haben P4/P8 womöglich schon andere Annahmen getroffen.
- **Fix:** Garantien jetzt in AD-8 aufnehmen, Retry-Parameter weiter deferred lassen. Zum Beispiel: „Zustellung in Reihenfolge des Schreibens pro Modul-Outbox, aber ohne Garantie nach Retries. Jedes Integration Event trägt Event-Id und fachlichen Zeitpunkt; Konsumenten mit reihenfolgeabhängiger Logik werten den Zeitpunkt aus. Nach N Fehlversuchen wird ein Eintrag als ‚tot‘ markiert und blockiert nichts.“

### M-5 — Migrationen: wann, wie und wo die History-Tabelle liegt

- **Stelle:** AD-3 („eigene EF-Core-Migrationen“); ADR 0002 Consequences.
- **Problem:** Nicht festgelegt ist, wann Migrationen angewendet werden (beim Start per `MigrateAsync`, als Migration Bundle in einem Init-Container, manuell). Ebenso fehlen die Lage der `__EFMigrationsHistory`-Tabelle pro DbContext (Default ist das gemeinsame `public`-Schema, das sich mehrere Kontexte teilen würden), der Ort der Outbox-Tabelle im Modell (Kernel-Konfiguration, in jedem Modul-DbContext eingebunden) und die Reihenfolge beim Start mehrerer Module. Jedes Modul wird das sonst eigenständig konfigurieren.
- **Fix:** Konvention ergänzen: „Jeder Modul-DbContext setzt `HasDefaultSchema(<modul>)` und `MigrationsHistoryTable("__ef_migrations_history", <modul>)`, bindet die Outbox-Konfiguration aus dem Kernel über eine Extension ein und wird beim Start des Hosts vor dem Öffnen der Endpoints migriert (oder: als Migration Bundle im Compose-Init-Container).“ Eine Variante wählen. Snake_case-Namen brauchen zudem eine Naming-Convention, etwa `EFCore.NamingConventions`; das Paket gehört in den Stack (siehe L-5).

### M-6 — Durchsetzung der Regeln fehlt (Architektur-Tests, Analyzer)

- **Stelle:** AD-2, AD-4, AD-6, AD-10, AD-11, AD-14.
- **Problem:** AD-2 wird durch Projektreferenzen erzwungen. Für die anderen Regeln gibt es keinen Mechanismus: kein `SaveChanges` in Domain-Event-Handlern (AD-6), kein Aufruf fremder Contracts-Commands aus Domain-Event-Handlern (AD-14), kein `DateTime.Now`/`UtcNow` (AD-11), Feature-Ordnerstruktur (AD-4). „Exhaustive switch ohne Discard“ (AD-10) bricht den Build nur, wenn CS8509/CS8524 als Fehler behandelt werden. Gerade für Agenten als Umsetzer sind maschinell geprüfte Regeln entscheidend.
- **Fix:** Konvention „Durchsetzung“ ergänzen: `TreatWarningsAsErrors` (mindestens für CS8509), `Microsoft.CodeAnalysis.BannedApiAnalyzers` mit `BannedSymbols.txt` (`DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now/UtcNow`, `Guid.NewGuid`), sowie Architektur-Tests (z. B. NetArchTest oder ArchUnitNET) im Testprojekt für AD-2, AD-4, AD-6 und AD-14. Den Bezug in jeder AD als „Enforced by“ notieren.

### M-7 — HTTP-API-Konventionen fehlen

- **Stelle:** Consistency Conventions (nur ProblemDetails); AD-4; Deferred „UI“.
- **Problem:** Jeder Slice mit Endpoint (P2 Read Models, P5, P6) entscheidet sonst selbst über Routenschema (Plural, Umlaute gemäss ADR 0003, Präfix `/api`?), JSON-Casing, Format von Zeitparametern (`von`/`bis` als ISO 8601 mit Offset?), Rückgabe bei leerem Aktuellem Zustand (Spec: „vor der ersten Messung leer“: 404 `NotFoundError` oder 200 mit leerem Body oder 204?), Statuscodes für erfolgreiche Commands (200/201/202/204), OpenAPI-Nutzung. Das UI ist zwar deferred, der API-Vertrag aber nicht. Zudem hat das Template `UseHttpsRedirection` aktiv, während TLS-Terminierung im LAN/Tailscale offen ist.
- **Fix:** Konventionstabelle ergänzen: Routen `/api/<ressource-plural>` klein, Umlaute ersetzt; JSON camelCase; Zeitpunkte ISO 8601 UTC; `von`/`bis` als Query-Parameter, halboffen [von, bis) passend zur Zeitabschnitt-Semantik; „kein Aktueller Zustand“ → 404 via `NotFoundError` (oder 204, entscheiden); Commands → 204 bzw. 201 mit Location; OpenAPI pro Endpoint. HTTPS: festlegen, ob TLS über Tailscale (`tailscale serve`) oder gar nicht; dann `UseHttpsRedirection` entfernen.

### M-8 — Test-Seam: Outbox, Zeit und Datenbank-Isolation nicht deterministisch

- **Stelle:** Tests-Konvention; AD-8, AD-11; Spec P2 „Testing Decisions“ (Neustart-Tests, Event-Empfänger).
- **Problem:** Mit K-1 laufen die beobachtbaren Events über den asynchronen Outbox-Worker. Ohne Regel bauen Tests `Task.Delay`/Polling ein, sind flaky oder unterscheiden sich pro Modul. Ebenso offen: ob Tests `FakeTimeProvider` verwenden (AD-11 macht es möglich, sagt es aber nicht), ob die Datenbank pro Test neu ist oder zurückgesetzt wird (Respawn, Container pro Klasse), und wie Neustart-Tests den Host neu aufbauen.
- **Fix:** Tests-Konvention ergänzen: „Im Test ersetzt ein `FakeTimeProvider` den `TimeProvider`. Der Outbox-Worker läuft im Test nicht periodisch; ein Test-Treiber `OutboxLeeren()` stellt synchron zu. Ein PostgreSQL-Container pro Testlauf, Zurücksetzen zwischen Tests per Respawn (oder Datenbank pro Testklasse). Neustart = neue `WebApplicationFactory` gegen denselben Connection String.“

### M-9 — Betrieb, Umgebungen, Secrets und Broker-Sicherheit nicht entschieden

- **Stelle:** Konfiguration-Konvention („Werte aus Umgebungsvariablen“), AD-13, Deployment-Diagramm, Deferred „Backup und CI“.
- **Problem:** Folgende Dimensionen sind weder entschieden noch deferred noch als offene Frage markiert:
  - **Umgebungen:** Welche gibt es (lokal Dev mit Compose? Aspire AppHost? nur Pi-Produktion)? Wie unterscheiden sie sich (`ASPNETCORE_ENVIRONMENT`, Seed-Daten)?
  - **Deployment:** Wo und wie wird das ARM64-Image gebaut (SDK-Container-Build ist im Template aktiv), über welche Registry oder welchen Weg kommt es auf den Pi, mit welchen Restart-Policies, Healthchecks (`/health`, Compose `depends_on: condition: service_healthy`) und Ressourcenlimits?
  - **Secrets:** Telegram-Bot-Token, DB-Passwort, Midea-Token/Key des Hilfsdienstes. „Umgebungsvariablen aus Compose“ heisst ohne Regel: Klartext in `docker-compose.yml` im Repo.
  - **Mosquitto:** Anonymer Zugriff? Port im LAN offen? Ohne Auth kann jedes Gerät im LAN Messungen fälschen oder Befehle senden, was AD-13 („nur LAN“) faktisch aushebelt.
  - **Aspire Dashboard:** Zugriff mit Browser-Token oder unauthentifiziert? Laut AD-13 ist das LAN die Vertrauensgrenze.
  - **Zeitzone im Container:** AD-11 braucht `Europe/Zurich`. Ob tzdata im Base-Image vorhanden ist, hängt von der Image-Variante ab (chiseled/Alpine).
- **Fix:** Abschnitt „Betrieb & Sicherheit“ ergänzen mit Entscheiden oder expliziten Deferred-/Open-Einträgen. Vorschlag: Secrets in einer nicht versionierten `.env` bzw. als Docker-Secrets, im Repo nur `.env.example`. Mosquitto mit Benutzer/Passwort und ACL, Port nur im Compose-Netz (kein Host-Port). PostgreSQL ohne Host-Port. Dashboard nur über Tailscale. Healthchecks für alle Dienste. Base-Image mit tzdata festlegen und mit einem Test auf `TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich")` absichern. Umgebungen: „Dev = Compose lokal, Prod = Pi; keine Staging-Umgebung“ als bewusster Entscheid.

### M-10 — AD-3: Lesen fremder Daten über „synchrone Commands“ widerspricht CQRS (AD-4) und AD-2

- **Stelle:** AD-3 („Daten anderer Module erhält es ausschliesslich über Integration Events oder synchrone Commands aus deren Contracts“), AD-2 (Contracts enthält nur Integration Events und Commands), AD-4.
- **Problem:** Braucht ein Modul synchron Daten eines anderen (z. B. Steuerung P5 prüft Fähigkeiten aus P1, bevor ein Befehl angenommen wird), bleibt laut Spine nur ein *Command*. Commands ändern aber laut AD-4 Zustand und laufen durch die Transaktion. Entweder entstehen „Commands“, die nur lesen (Verletzung von AD-4), oder Module bauen sich eigene Kopien. Beides wird ohne Regel unterschiedlich gelöst.
- **Fix:** Eines von beiden explizit festlegen: (a) `<Modul>.Contracts` darf auch Queries enthalten; sie laufen ohne Transaction Behavior und lesen nur das eigene Schema des Zielmoduls. AD-2 und AD-3 entsprechend anpassen. Oder (b) Module halten benötigte fremde Daten als lokale Kopie, nachgeführt über Integration Events. Synchrone Contracts sind dann ausschliesslich schreibend. Das Wort „Daten erhält“ in AD-3 entsprechend präzisieren.

---

## Low

### L-1 — Abhängigkeit Klimageraet → Steuerung.Contracts unbegründet

- **Stelle:** Mermaid-Diagramm unter „Invariants & Rules“ (`K --> SC`).
- **Problem:** Keine AD und kein Prozess begründet, warum Klimageraet Steuerung.Contracts kennt. Zusammen mit `S --> KC` entsteht eine gegenseitige Kopplung der beiden Module (zwar kein Projektzyklus, aber ein konzeptioneller), und die Richtung „Steuerung ist Downstream von Klimageraet“ (AD-9) verwischt.
- **Fix:** Kante entfernen oder mit dem konkreten Bedarf begründen (welches Integration Event von Steuerung braucht Klimageraet?). Falls P8 etwas braucht („Messung zeigt meinen Befehl“, Spec P2 Hotspot), das als offene Frage notieren statt die Abhängigkeit vorwegzunehmen.

### L-2 — AD-6-Ablauf berücksichtigt AD-14 nicht

- **Stelle:** AD-6 Rule und Sequenzdiagramm („Transaktion starten → … → Commit“).
- **Problem:** Beim beigetretenen Command (AD-14) gibt es weder „Transaktion starten“ noch „Commit“. Wer nur AD-6 liest, implementiert ein Behavior, das immer committet.
- **Fix:** In AD-6 ergänzen: „Starten und Commit nur im äussersten Transaction Behavior (AD-14); beigetretene Commands durchlaufen nur Handler → SaveChanges → Dispatch.“ Ausserdem eine Obergrenze für die Dispatch-Schleife festlegen (z. B. maximal 10 Runden, danach Exception), damit sich gegenseitig auslösende Handler nicht endlos laufen.

### L-3 — Serialisierung der Outbox-Einträge ist faktisch nicht deferrable

- **Stelle:** Deferred „Versionierung von Integration Events und Contract-Commands“.
- **Problem:** Die Versionierung darf warten, das gespeicherte Format nicht: Persistierte Outbox-Einträge überleben Deployments. Speichert der erste Worker den CLR-Typnamen (assembly-qualified), bricht jede Umbenennung oder Verschiebung offene Einträge.
- **Fix:** In AD-8 festhalten: Outbox-Einträge als JSON mit stabilem, explizit vergebenem Typ-Schlüssel (z. B. `klimageraet.solltemperatur-geaendert.v1`), nie mit CLR-Typnamen. Die Versionierung selbst bleibt deferred.

### L-4 — AD-12 vs. AD-11: `Guid.CreateVersion7()` liest die Systemuhr

- **Stelle:** AD-11 („Zeit kommt nur aus `TimeProvider`“), AD-12.
- **Problem:** `Guid.CreateVersion7()` ohne Argument liest `DateTimeOffset.UtcNow`. Formal ist das ein Verstoss gegen AD-11, und in Tests mit `FakeTimeProvider` sind IDs nicht zeitlich konsistent.
- **Fix:** AD-12 präzisieren: IDs über eine Kernel-Fabrik `Guid.CreateVersion7(timeProvider.GetUtcNow())`. Das Bannen von `Guid.NewGuid` und der parameterlosen Überladung gehört zu M-6.

### L-5 — Stack unvollständig für die festgelegten Konventionen

- **Stelle:** Stack-Tabelle.
- **Problem:** Es fehlen die MQTT-Client-Bibliothek (z. B. MQTTnet), der Telegram-Zugang (Bibliothek oder roher HTTP-Client), `EFCore.NamingConventions` für snake_case (Konvention „Schemas und Tabellen“) und das Werkzeug für Architektur-Tests (M-6). Weil nur je ein Modul MQTT bzw. Telegram nutzt, ist das Divergenzrisiko gering. Die snake_case-Konvention ohne Paket führt aber zu handgeschriebenen `ToTable`/`HasColumnName` je Slice. Ausserdem zielt Testcontainers 4.14 laut Memlog bis `net10.0`. Die Kompatibilität mit `net11.0` ist plausibel (netstandard/net10-Assets), aber unbestätigt.
- **Fix:** Stack ergänzen: MQTTnet (Version), Telegram-Zugang (Entscheid), `EFCore.NamingConventions` (Version passend zu EF Core 11) oder Konvention „Namen per ModelBuilder-Konvention im Kernel“. Den Testcontainers-Hinweis als Risiko vermerken.

### L-6 — Übergang vom bestehenden Template-Projekt zur Structural Seed

- **Stelle:** Structural Seed (`Backend/src/Host`, `Backend/tests/Backend.Tests`); Repo-Ist-Zustand `Backend/Backend/Backend.csproj` (Template mit `weatherforecast`, `UseHttpsRedirection`, OpenAPI, Container-Build-Einstellungen, UserSecretsId).
- **Problem:** Die Spine sagt nicht, ob `Backend/Backend` zu `src/Host` umbenannt oder neu angelegt wird und welche Template-Einstellungen bleiben (Container-Port 8081, `ContainerBaseImage`, UserSecrets vs. Konvention „Umgebungsvariablen“). Der P2-Slice muss das sonst nebenbei entscheiden.
- **Fix:** In der Structural Seed einen Satz ergänzen: „`Backend/Backend` wird zu `Backend/src/Host` verschoben; `weatherforecast` und `UseHttpsRedirection` entfallen; Container-Einstellungen bleiben im Host-csproj; UserSecrets nur für lokale Entwicklung.“

---

## Was gut ist (bleibt so)

- AD-6/AD-7/AD-8 bilden zusammen ein schlüssiges, widerspruchsfreies Modell: Dispatch in SavingChanges, keine Kaskaden, keine Seiteneffekte in der Transaktion. Die Prevents-Formulierungen sind konkret und treffen echte Fehlerbilder.
- AD-2 ist über Projektreferenzen hart durchsetzbar, und die Datenhoheit (AD-3) ist klar.
- AD-10 mit Union und exhaustivem Switch ist eine starke, compilergestützte Regel (mit M-6 vollständig durchsetzbar).
- AD-11 deckt die Zeitlogik von P3, P7 und P8 (Europe/Zurich, Tageswechsel 00:00) vorausschauend ab.
- Deferred-Einträge sind überwiegend korrekt begründet (zentraler Mechanismus = keine Divergenz). Ausnahmen sind M-4 und L-3.
- Begriffe sind durchgehend konsistent mit CONTEXT.md und ADR 0003. Einen Widerspruch zur Fachsprache habe ich nicht gefunden.
