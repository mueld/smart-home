---
type: architecture-review
reviews: ../ARCHITECTURE-SPINE.md
perspective: adversarial (Gegner)
created: '2026-11-11'
---

# Adversarieller Review – Architecture Spine smart-home Backend

## Methode

Für jedes Loch werden zwei Units eine Ebene unter der Spine konstruiert (Slices, Module, Handler, Worker, Trigger), die **jede AD buchstabengetreu einhalten** und trotzdem nicht zusammenpassen. Jedes Paar zeigt eine Stelle, an der die Spine zwei Bauweisen offen lässt, die zur Laufzeit kollidieren. Für jedes Loch wird eine neue AD oder eine Verschärfung einer bestehenden AD vorgeschlagen.

## Gesamturteil

Die Spine ist bei den Modulgrenzen, der Pipeline-Reihenfolge und der Outbox-Grundidee solide. Sie lässt aber den **Transaktionsrand rund um AD-8/AD-14** (wer committet, wer `SaveChanges` aufruft, was beim Fehler-Result einer beigetretenen Transaktion geschieht, wie Konsumenten von Integration Events in die Pipeline kommen) und die **Mechanik von „verträgt Duplikate“** offen. Genau dort können zwei regelkonforme Slices stillschweigend Teilschreibungen committen, Events doppelt verarbeiten oder Commits verlieren. Drei Löcher sind critical, sechs high.

## Übersicht

| # | Loch | Schwere | Betroffene AD |
| --- | --- | --- | --- |
| L1 | Konsumenten von Integration Events liegen ausserhalb der Pipeline; Worker-Transaktion unklar | critical | AD-5, AD-6, AD-8, AD-14 |
| L2 | Fehler-Result oder abgefangene Exception in einer beigetretenen Transaktion | critical | AD-10, AD-14 |
| L3 | Reentrancy: zyklische synchrone Aufrufe über denselben Scope und DbContext | critical | AD-2, AD-14 |
| L4 | „Verträgt Duplikate“ ohne Mechanismus | high | AD-8, AD-9, AD-12 |
| L5 | Parallele Commands auf dasselbe Aggregate (MQTT, P1, Contract-Commands) | high | AD-4, AD-9 |
| L6 | Zwei Wege, den Zustand des Klimageräts zu ändern; zwei Einstiege für Befehle | high | AD-1, AD-4, AD-9 |
| L7 | Fachlicher Zeitpunkt vs. Verarbeitungszeitpunkt; Reihenfolge bei at-least-once | high | AD-8, AD-11 |
| L8 | Zeit-Trigger: direkter Fremd-Command, Flanke vs. Zustand | high | AD-11, AD-14 |
| L9 | Sommerzeit-Umstellung bei fachlichen Zeiten | high | AD-11 |
| L10 | Identität des Klimageräts / zwei Erzeuger desselben Aggregates | high | AD-1, AD-12 |
| L11 | Form von Integration Events und Outbox-Einträgen | medium | AD-2, AD-6, AD-8 |
| L12 | Wer ruft `SaveChanges` auf | medium | AD-5, AD-6 |
| L13 | DbContext-Registrierung, Scope-Lebensdauer, Execution Strategy | medium | AD-3, AD-14 |
| L14 | Result-Semantik ausserhalb von HTTP; fehlende Fehlerfälle | medium | AD-10 |
| L15 | Fremde Daten lesen: Contracts ohne Queries | medium | AD-2, AD-3 |
| L16 | Veraltete Aufträge an Umsysteme | medium | AD-8, AD-9 |
| L17 | Format und Präzision des Zeitpunkts der Abfrage | medium | AD-11 |
| L18 | Interceptor-Schleife und Change Detection | low | AD-6 |
| L19 | Pflege der Read Models | low | AD-4 |

---

## L1 — Konsumenten von Integration Events liegen ausserhalb der Pipeline (critical)

**Szenario**

- **Unit A – Benachrichtigung, Konsument `FehlercodeGemeldet`:** Der Handler ist als `IIntegrationEventHandler<FehlercodeGemeldet>` registriert. Da AD-5 die Pipeline nur für Commands definiert und das Transaction Behavior den DbContext „anhand des Commands“ auflöst, gibt es für ein Integration Event keinen DbContext und keine Transaktion. Der Handler schreibt den Telegram-Auftrag in die Outbox und ruft selbst `SaveChanges` auf. Das ist regelkonform: AD-6 verbietet `SaveChanges` nur Domain-Event-Handlern, und AD-8 verlangt nur „in einer eigenen Transaktion“, was ein implizites `SaveChanges` erfüllt.
- **Unit B – Steuerung, Konsument `SolltemperaturGeaendert` (P8):** Der Handler ruft über den Mediator den modul-eigenen Command `UebersteuerungErkennen` auf. Die Pipeline greift.
- **Worker:** AD-14 sagt, alle Modul-DbContexts eines Scopes teilen eine Verbindung, und das Transaction Behavior tritt einer laufenden Transaktion bei. Ein Worker, der den Outbox-Eintrag mit `SELECT … FOR UPDATE` in einer Transaktion beansprucht und danach zustellt, bewirkt, dass Unit B dieser Worker-Transaktion **beitritt**. Das ist regelkonform, denn der Worker ist kein Transaction Behavior. Ein anderer Worker committet die Beanspruchung zuerst.

**Wo es bricht**

- Mit Worker-Variante 1 wird Unit B nie selbst committet: „Nur das äusserste Transaction Behavior committet“, und das äusserste ist hier der Worker. Ob überhaupt committet wird, hängt von Code ab, den keine AD regelt. Stellt der Worker ein Event an zwei Konsumenten zu (Steuerung und Benachrichtigung) und wirft der zweite, rollt der erste mit zurück, oder er bleibt committet, und beim Retry wird er erneut ausgeführt.
- Unit A und Unit B haben unterschiedliche Fehler-, Validierungs- und Tracing-Semantik (Unit A: kein Span, keine Validation, kein Rollback bei Fehler-Result).

**Fix (Verschärfung AD-8, neue Rule AD-15)**

> **AD-15 — Konsumenten von Integration Events:** Ein Integration-Event-Handler ist ein dünner Adapter im konsumierenden Modul: Er übersetzt das Event in genau **einen Command des eigenen Moduls** und sendet ihn über den Mediator (volle Pipeline). Er greift selbst nie auf einen DbContext zu.
> **Ergänzung AD-8:** Der Worker stellt jeden Outbox-Eintrag **pro Konsument** einzeln zu (Zustellstatus pro Paar Eintrag/Konsument). Jede Zustellung läuft in einem **neuen DI-Scope ohne laufende Transaktion**, sodass der Konsumenten-Command die äusserste Transaktion startet. Beanspruchen und Als-zugestellt-Markieren des Eintrags sind eigene, kurze Transaktionen des Workers; der Konsument tritt ihnen nie bei.

---

## L2 — Fehler-Result oder abgefangene Exception in einer beigetretenen Transaktion (critical)

**Szenario**

- **Unit A – Klimageraet, Handler `SolltemperaturSetzen` (Contract-Command):** Der Handler lädt das Aggregate, ändert es, ruft `SaveChanges` auf (AD-6: Handler → `SaveChanges`) und stellt erst danach fest, dass eine Fachregel verletzt ist (z. B. Temperatur ausserhalb der Fähigkeiten aus P1). Er gibt `ValidationError` zurück. Das beigetretene Behavior committet nicht und rollt nicht zurück; AD-14 lässt ihm nur „committet nie selbst“.
- **Unit B – Steuerung, Handler `ZeitabschnittBeginnen`:** Er ruft `SolltemperaturSetzen` auf, wertet `ValidationError` als „Gerät kann das nicht, egal“ aus, setzt seinen eigenen Zustand weiter und gibt `Success` zurück. Das ist regelkonform: AD-10 schreibt nicht vor, dass Fehler-Results propagiert werden.
- **Variante mit Exception:** Unit A wirft bei einer Unique-Verletzung `DbUpdateException`. Unit B fängt sie, um sie in ein Result umzuwandeln. Kein AD verbietet das.

**Wo es bricht**

- Das äusserste Behavior sieht `Success` und committet. Die bereits per `SaveChanges` geschriebenen Änderungen von Unit A, inklusive Outbox-Eintrag mit MQTT-Befehl, werden **committet, obwohl Unit A einen Fehler gemeldet hat**. Das ist eine halb ausgeführte modulübergreifende Operation, genau das, was AD-14 verhindern will.
- Bei der abgefangenen Exception ist die PostgreSQL-Transaktion im Zustand „aborted“. Jeder weitere Befehl schlägt fehl, und der Commit führt zu einem Rollback, obwohl der Handler `Success` meldet. Der Endpoint antwortet je nach Treiberverhalten mit 200 oder 500, gespeichert ist nichts.

**Fix (Verschärfung AD-14 und AD-10)**

> **Ergänzung AD-14:** Ein beigetretenes Transaction Behavior setzt vor dem Handler einen **Savepoint** und rollt bei Fehler-Result oder Exception auf diesen Savepoint zurück, inklusive Zurücksetzen des Change Trackers seines DbContexts. Damit bleibt ein Fehler-Result eines Contract-Commands ohne Wirkung, egal wie der Aufrufer reagiert.
> **Ergänzung AD-10:** Exceptions aus `mediator.Send` werden nie abgefangen, um weiterzuarbeiten; sie propagieren bis zum äussersten Behavior bzw. zum globalen Exception Handler. Datenbank-Constraints dienen nicht als Kontrollfluss innerhalb eines Handlers.

---

## L3 — Reentrancy über zyklische synchrone Aufrufe (critical)

**Szenario**

Das Abhängigkeitsdiagramm erlaubt ausdrücklich `Klimageraet → Steuerung.Contracts` und `Steuerung → Klimageraet.Contracts`. AD-2 erlaubt in Contracts Commands für synchrone Aufrufe, AD-14 erlaubt sie aus jedem Command-Handler.

- **Unit A – Steuerung, `ManuellSteuern`:** Lädt Aggregate `Zeitplan`, ändert es (noch nicht gespeichert), ruft `Klimageraet.Contracts.SolltemperaturSetzen` auf.
- **Unit B – Klimageraet, Handler `SolltemperaturSetzen`:** Um „Ausgesetzt“ sofort zu setzen, ruft er `Steuerung.Contracts.ZeitplanAussetzen` auf. Beides regelkonform.

**Wo es bricht**

`ZeitplanAussetzen` läuft im **selben Scope**, also mit derselben Instanz des Steuerung-DbContexts. Er bekommt aus dem Change Tracker das bereits veränderte, ungespeicherte `Zeitplan`-Objekt von Unit A. Sein `SaveChanges` speichert die halbfertigen Änderungen von Unit A mit und dispatcht deren Domain Events, **bevor Unit A fertig ist**. Danach ruft Unit A erneut `SaveChanges` auf, und die Events sind schon weg oder werden doppelt erzeugt. Ohne Zyklus ist das gleiche Muster möglich, sobald ein Modul in einem Scope zweimal als Command-Eigentümer auftritt.

**Fix (Verschärfung AD-2 und AD-14)**

> **Ergänzung AD-2:** Synchrone Aufrufe (Contract-Commands) haben eine feste, azyklische Richtung: **Steuerung → Klimageraet**, **Steuerung/Klimageraet → Benachrichtigung** (falls nötig). Klimageraet referenziert `Steuerung.Contracts` nur für Integration Events, nie für Commands.
> **Ergänzung AD-14:** Das Transaction Behavior wirft eine Exception, wenn ein Command eines Moduls aufgerufen wird, während im selben Scope bereits ein Command desselben Moduls läuft (Reentrancy-Schutz).

---

## L4 — „Verträgt Duplikate“ ohne Mechanismus (high)

**Szenario**

- **Unit A – Steuerung, Konsument `Ausgeschaltet` (P8):** Dedupliziert über eine Tabelle `verarbeitete_events` mit der Event-ID.
- **Unit B – Benachrichtigung, Konsument `FehlercodeGemeldet` (P4):** Dedupliziert über den fachlichen Zustand („letzter gemeldeter Fehlercode“). Das Event hat gar keine ID, weil der Produzent keine gesetzt hat; AD-12 gilt nur für Entitäten.
- **Unit C – Klimageraet, MQTT-Auftrag `SelbstreinigungStarten`:** Wird als Umschalt-Befehl („toggle“) gesendet.

**Wo es bricht**

- Unit A kann bei Events ohne ID nicht deduplizieren; Unit B unterdrückt einen echten zweiten Fehlercode-Vorfall (Fehler 5, behoben, Fehler 5 erneut), weil das Kriterium fachlich falsch ist.
- Bei Unit C schaltet die zweite Zustellung desselben Auftrags die Selbstreinigung wieder aus. Telegram (AD-8 „Aufträge an Umsysteme“) kann Duplikate gar nicht vertragen; die Rule „jeder Konsument verträgt Duplikate“ ist dort unerfüllbar.

**Fix (Verschärfung AD-8, Ergänzung AD-9)**

> **Ergänzung AD-8:** Jedes Integration Event trägt eine `EventId` (GUID v7, beim Schreiben in die Outbox erzeugt) und einen fachlichen Zeitpunkt. Jedes konsumierende Modul führt eine **Inbox-Tabelle** im eigenen Schema; der Konsumenten-Command schreibt `(EventId, Konsument)` in derselben Transaktion wie seine Wirkung und bricht ab, wenn der Eintrag existiert. Für Telegram-Aufträge sind Duplikate ausdrücklich akzeptiert (at-least-once, keine Deduplizierung beim Empfänger).
> **Ergänzung AD-9:** Befehle ans Klimagerät sind **absolut und idempotent** formuliert (Zielzustand, nie „umschalten“).

---

## L5 — Parallele Commands auf dasselbe Aggregate (high)

**Szenario**

- **Unit A – MQTT-Adapter Klimageraet:** Verarbeitet jede eingehende Nachricht in einem eigenen Task (MQTTnet-Handler laufen parallel), jede in eigenem Scope mit `MessungAufnehmen`.
- **Unit B – P1 `FaehigkeitenAktualisieren`** oder ein Contract-Command aus Steuerung: ändert dasselbe Aggregate Klimagerät gleichzeitig. Keine AD verlangt Serialisierung oder ein Concurrency Token.

**Wo es bricht**

Zwei Messungen (z. B. ein QoS-1-Duplikat und die Originalnachricht) laden beide dieselbe „letzte Messung“, beide gelten als neu, beide erzeugen `SolltemperaturGeaendert` – Verstoss gegen Spec P2 Story 26. Bei Unit B gewinnt das letzte `SaveChanges` (lost update der Vergleichsbasis).

**Fix (neue Rule AD-16)**

> **AD-16 — Nebenläufigkeit:** Jedes Aggregate hat ein Concurrency Token (PostgreSQL `xmin`). Ein Konflikt ist eine Exception (AD-10) und wird vom Aufrufer nach einer einheitlichen Regel behandelt: Endpoint → 409 bzw. 500, Outbox-Konsument und MQTT-Eingang → Retry. Eingehende Messungen werden pro Klimagerät **sequenziell** verarbeitet.

---

## L6 — Zwei Wege, den Zustand des Klimageräts zu ändern (high)

**Szenario**

- **Unit A – Klimageraet, Handler `SolltemperaturSetzen` (P5/P7):** Setzt im Aggregate sofort `Zustand.Solltemperatur = 22` (Commands ändern Zustand über das Aggregate, AD-4) und schreibt den MQTT-Auftrag in die Outbox.
- **Unit B – Klimageraet, Handler `MessungAufnehmen` (P2):** Vergleicht die neue Messung mit der „letzten Messung“ im Aggregate.
- **Unit C – Klimageraet, Endpoint `POST /klimageraet/solltemperatur`**, gleichzeitig **Unit D – Steuerung, Endpoint `ManuellSteuern`** (P5), der den Contract-Command aufruft. AD-1 ordnet „Befehle ans Klimagerät“ Klimageraet und P5 Steuerung zu; beide Einstiege sind regelkonform.

**Wo es bricht**

- Mit Unit A wird die Vergleichsbasis durch einen Befehl statt durch eine Beobachtung geändert: Die nächste Messung zeigt 22 °C und erzeugt **kein** `SolltemperaturGeaendert`. Wirkt der Befehl nicht (Gerät offline), steht im Aktuellen Zustand ein Wert, den das Gerät nie hatte. Lässt man Unit A weg, erzeugt der eigene Befehl der Steuerung ein `SolltemperaturGeaendert`, das P8 als Übersteuerung deutet (Echo).
- Mit Unit C und Unit D gibt es zwei Wege für manuelle Befehle; Steuerung kennt nur die über Unit D und kann die Quelle nicht konsistent zuordnen.

**Fix (Verschärfung AD-9, Ergänzung AD-1)**

> **Ergänzung AD-9:** Der beobachtete Zustand des Aggregates Klimagerät ändert sich **ausschliesslich durch `MessungAufnehmen`**. Contract-Commands erzeugen nur einen Befehl (Outbox-Auftrag) und das Event `…BefehlGesendet` mit `BefehlId` und Zielwerten, ändern aber nie den beobachteten Zustand.
> **Ergänzung AD-1:** Alle Endpoints, die das Klimagerät steuern, liegen in Steuerung (P5). Klimageraet bietet keine schreibenden HTTP-Endpoints. Die Zuordnung „eigener Befehl vs. Fernbedienung“ (P8) liegt in Steuerung und stützt sich auf die eigenen gesendeten Befehle.

---

## L7 — Fachlicher Zeitpunkt vs. Verarbeitungszeitpunkt; Reihenfolge (high)

**Szenario**

- **Unit A – Steuerung, Konsument `SolltemperaturGeaendert` (P8):** Berechnet „ausgesetzt bis 00:00“ aus `TimeProvider.GetUtcNow()` – regelkonform nach AD-11 („Zeit kommt nur aus TimeProvider“).
- **Unit B – Konsument derselben Events:** verwendet `OccurredAt` aus dem Event, das der Produzent mit `TimeProvider` beim Verarbeiten gesetzt hat, nicht den Zeitpunkt der Abfrage.
- **Worker:** stellt bei Retry einen fehlgeschlagenen Eintrag später zu, während jüngere Einträge schon durch sind.

**Wo es bricht**

- Messung um 23:59 mit Übersteuerung, Zustellung nach Pi-Neustart um 00:03: Unit A setzt den Zeitplan für den **ganzen nächsten Tag** aus.
- `Ausgeschaltet` (Retry) kommt nach `Eingeschaltet` an; Steuerung hält das Gerät für aus. Bei P3 kommt „wieder online“ vor „offline“ als Telegram an.

**Fix (Verschärfung AD-8 und AD-11)**

> **Ergänzung AD-11:** Fachliche Entscheide in Konsumenten verwenden den **fachlichen Zeitpunkt aus dem Event** (bei Klimageraet-Events den Zeitpunkt der Abfrage der auslösenden Messung), nie den Zeitpunkt der Verarbeitung. `TimeProvider` liefert nur „jetzt“ für Trigger und technische Zeitstempel.
> **Ergänzung AD-8:** Der Worker stellt Einträge eines Moduls pro Konsument **in Reihenfolge (FIFO)** zu; ein fehlschlagender Eintrag blockiert die nachfolgenden für diesen Konsumenten. Integration Events tragen zusätzlich eine pro Aggregate monotone Sequenznummer, damit Konsumenten Veraltetes erkennen können.

---

## L8 — Zeit-Trigger: direkter Fremd-Command, Flanke vs. Zustand (high)

**Szenario**

- **Unit A – Steuerung, Zeit-Trigger P7:** Ein `BackgroundService` ruft um 06:00 direkt `Klimageraet.Contracts.EinschaltenMitVorgaben` auf (AD-14 erlaubt Contract-Commands aus Zeit-Triggern) und speichert danach in einem eigenen Command „Zeitabschnitt gestartet“.
- **Unit B – Steuerung, Zeit-Trigger P8 Wiederaufnahme um 00:00:** sendet den modul-eigenen Command `ZeitplanWiederaufnehmen`, der intern den Contract-Command aufruft.

**Wo es bricht**

- Bei Unit A ist der Fremd-Command die äusserste Transaktion; die eigene Markierung ist eine zweite Transaktion. Absturz dazwischen → nach Neustart wird erneut gesendet oder die Markierung fehlt; Steuerung und Klimageraet sind nicht konsistent – genau das, was AD-14 verhindern soll.
- Beide Trigger feuern auf eine **Flanke** (genau 06:00 / 00:00). Ist der Pi um 00:00 neu gestartet, findet die Wiederaufnahme nie statt; AD-11 verlangt nur Testbarkeit, nicht Nachholbarkeit.

**Fix (Verschärfung AD-14, neue Rule AD-17)**

> **Ergänzung AD-14:** Endpoints, Zeit-Trigger und Outbox-Konsumenten senden nur **Commands des eigenen Moduls**. Contract-Commands fremder Module werden nur aus Command-Handlern aufgerufen, damit immer das aufrufende Modul die äusserste Transaktion besitzt.
> **AD-17 — Zeit-Trigger:** Ein Zeit-Trigger ist zustandsbasiert: Er läuft periodisch (z. B. jede Minute), berechnet den Sollzustand für „jetzt“ und gleicht ihn mit dem persistierten Ist ab. Er ist idempotent und holt verpasste Zeitpunkte nach einem Neustart nach.

---

## L9 — Sommerzeit-Umstellung bei fachlichen Zeiten (high)

**Szenario**

- **Unit A – Steuerung, Zeitplan-Ausführung:** Wandelt `jetzt (UTC)` nach Europe/Zurich und nimmt `local.Hour` als Index in die Temperaturkurve.
- **Unit B – Steuerung, Planung des nächsten Triggers:** Baut die lokale Zeit „heute 02:00“ und rechnet sie mit `TimeZoneInfo.ConvertTimeToUtc` nach UTC.
- **Unit C – Steuerung, „Ausgesetzt“:** Unit C1 speichert `AusgesetztBis` als UTC-Zeitpunkt der nächsten lokalen Mitternacht, Unit C2 speichert ein lokales `DateOnly` „ausgesetzt am“. Beides ist mit AD-11 vereinbar (AD-11 regelt nur Zeitpunkte).

**Wo es bricht**

- Letzter Sonntag im März: 02:00 lokal existiert nicht → Unit B wirft `ArgumentException`; Unit A findet die Stunde 02 nie, die Kurve springt. Letzter Sonntag im Oktober: 02:00–03:00 kommt zweimal vor → Unit A führt die Stunde doppelt aus, Zeitabschnitt 02:00–04:00 dauert 3 Stunden.
- C1 und C2 werden in derselben Abfrage verglichen und liefern an Umstellungstagen bzw. bei Verarbeitung nach Mitternacht unterschiedliche Ergebnisse.

**Fix (Verschärfung AD-11)**

> **Ergänzung AD-11:** Fachliche Uhrzeiten sind `TimeOnly` (`time`), Kalendertage `DateOnly` (`date`), beide in Europe/Zurich. Umrechnungen zwischen UTC und Ortszeit gehen ausschliesslich über eine `Kernel`-Komponente (z. B. `Ortszeit`) mit festen Regeln: nicht existierende Ortszeit → nächster gültiger Zeitpunkt; doppelte Ortszeit → erstes Auftreten. Eine Stunde der Temperaturkurve, die es am Tag nicht gibt, entfällt; eine doppelte wird einmal ausgeführt. „Ausgesetzt bis“ wird beim Aussetzen einmal als UTC-Zeitpunkt berechnet und gespeichert.

---

## L10 — Identität des Klimageräts, zwei Erzeuger desselben Aggregates (high)

**Szenario**

- **Unit A – P2 `MessungAufnehmen`:** Findet kein Aggregate und legt eines mit neuer GUID v7 an (AD-12).
- **Unit B – P1 `FaehigkeitenAktualisieren`:** Läuft beim Start vor der ersten Messung, findet keines und legt ebenfalls eines an.
- **Unit C – Steuerung:** Kennt die `KlimageraetId` aus der Konfiguration; Unit D liest sie aus `MessungAufgenommen`.

**Wo es bricht**

Es entstehen zwei Aggregates für dasselbe physische Gerät. Messungen und Fähigkeiten hängen an verschiedenen IDs; Steuerung adressiert je nach Unit das eine oder das andere und erhält `NotFoundError`.

**Fix (Ergänzung AD-12, neue Rule)**

> **Ergänzung AD-12:** Ein Aggregate hat neben der technischen GUID v7 einen eindeutigen **fachlichen Schlüssel**, wo es einen gibt (beim Klimagerät die Geräte-ID des Hilfsdiensts), abgesichert durch einen Unique-Index. Das Klimagerät wird genau einmal an einer definierten Stelle angelegt (Registrierung bzw. Seed aus der Konfiguration); andere Slices legen es nie implizit an. Contracts referenzieren das Klimagerät über eine Id, die Klimageraet veröffentlicht.

---

## L11 — Form von Integration Events und Outbox-Einträgen (medium)

**Szenario**

- **Unit A – Klimageraet:** Mappt das Domain Event `SolltemperaturGeaendert` in einem Domain-Event-Handler auf das gleichnamige Integration Event in `Klimageraet.Contracts`, mit einer Kopie des Enums `Modus` und `decimal`-Temperatur.
- **Unit B – ein späterer Slice in Klimageraet:** Fügt einen zweiten Domain-Event-Handler hinzu, der dasselbe Integration Event schreibt (z. B. im Rahmen von P7 „Befehl gewirkt“).
- **Outbox:** Klimageraet bildet die Outbox-Tabelle mit der Kernel-Konfiguration ab, Benachrichtigung definiert eine eigene Entity mit anderen Spalten. Serialisiert wird der .NET-Typname.

**Wo es bricht**

- Gleichnamige Domain- und Integration Events führen zu falschen `using`s; zwei Mapper erzeugen pro Messung zwei Outbox-Einträge.
- Eine Umbenennung oder Verschiebung der Event-Klasse macht offene Outbox-Einträge unlesbar (Typname nicht mehr auflösbar). Der zentrale Worker kann die abweichende Outbox von Benachrichtigung nicht lesen.

**Fix (Verschärfung AD-2 und AD-8)**

> **Ergänzung AD-2:** Integration Events und Contract-Commands sind `sealed record`s aus primitiven Typen, `DateTimeOffset`, `Guid` und eigenen Contract-Typen; Domain-Typen werden nie verwendet, Enums als String serialisiert. Integration Events heissen wie das fachliche Ereignis mit Suffix `IntegrationEvent` und haben einen expliziten, stabilen Typnamen (Attribut), der in der Outbox gespeichert wird.
> **Ergänzung AD-8:** Jedes Integration Event hat genau **einen** Domain-Event-Handler, der es schreibt (`Infrastructure/IntegrationEvents/`). Die Outbox-Entity und ihre Abbildung kommen ausschliesslich aus `Kernel` und werden in jedem Modul-DbContext über dieselbe Extension Method eingebunden.

---

## L12 — Wer ruft `SaveChanges` auf (medium)

**Szenario**

AD-6 nennt „Handler → `SaveChanges`“, das Sequenzdiagramm zeigt den Handler als Aufrufer, die Prevents-Zeile verbietet nur ein „zweites“ `SaveChanges`.

- **Unit A:** Handler ruft `SaveChanges` am Ende auf.
- **Unit B:** Handler ruft `SaveChanges` nicht auf, weil er annimmt, das Transaction Behavior tut es vor dem Commit; ein späterer Umbau des Behaviors fügt dies tatsächlich hinzu.
- **Unit C:** Handler ruft `SaveChanges` vor einem Contract-Command auf (um eine ID „sicher“ zu haben) und noch einmal danach.

**Wo es bricht**

Ohne Behavior-`SaveChanges` gehen die Änderungen von Unit B verloren (Commit ohne Schreibvorgang). Mit Behavior-`SaveChanges` laufen Unit A und Unit C in zwei bzw. drei Interceptor-Durchläufe; Domain-Event-Handler sehen Zwischenzustände.

**Fix (Verschärfung AD-6)**

> **Ergänzung AD-6:** Command-Handler rufen **nie** `SaveChanges` auf. Das Transaction Behavior ruft genau einmal `SaveChanges` auf dem DbContext seines Moduls auf, nachdem der Handler ein Erfolgs-Result geliefert hat, und committet danach (bzw. gibt bei beigetretener Transaktion den Savepoint frei, L2).

---

## L13 — DbContext-Registrierung, Scope-Lebensdauer, Execution Strategy (medium)

**Szenario**

- **Unit A – Klimageraet:** `AddDbContextPool` mit eigener Connection-String-Verbindung und `EnableRetryOnFailure()`, weil AD-10 transiente Fehler erwähnt.
- **Unit B – Steuerung:** `AddDbContext` mit gemeinsamer, scoped `NpgsqlConnection`.
- **Worker / MQTT-Adapter:** Ein `BackgroundService` erzeugt einmal beim Start einen Scope und verwendet ihn für alle Zustellungen.

**Wo es bricht**

Die retrying execution strategy wirft bei benutzerinitiierten Transaktionen; ein gepoolter Context mit eigener Verbindung kann der gemeinsamen Verbindung nach AD-14 nicht beitreten. Ein langlebiger Scope hält eine Verbindung dauerhaft offen, lässt den Change Tracker wachsen und überträgt verdorbenen Zustand nach einer fehlgeschlagenen Zustellung auf alle folgenden.

**Fix (Ergänzung AD-3 und AD-14)**

> **Ergänzung AD-3:** Modul-DbContexts werden ausschliesslich über eine `Kernel`-Extension (`AddModuleDbContext<T>`) registriert: kein Pooling, keine retrying execution strategy, Verbindung aus einer scoped `NpgsqlConnection`, Interceptor registriert.
> **Ergänzung AD-14:** Jeder Hintergrund-Einstieg (Outbox-Zustellung, Zeit-Trigger-Lauf, eingehende Messung) erzeugt einen **eigenen DI-Scope pro Arbeitseinheit**.

---

## L14 — Result-Semantik ausserhalb von HTTP; fehlende Fehlerfälle (medium)

**Szenario**

- **Unit A – MQTT-Eingang:** `MessungAufnehmen` liefert bei Duplikat `Success` (No-op).
- **Unit B – derselbe Command nach einem Umbau:** liefert bei Duplikat `ValidationError`; der Adapter loggt einen Fehler.
- **Unit C – Benachrichtigung-Konsument:** liefert `ValidationError` bei einem Event, das er nicht verarbeiten kann; **Unit D** wirft stattdessen eine Exception.
- **Unit E – Steuerung-Endpoint** reicht `NotFoundError` aus `Klimageraet` (Gerät unbekannt) unverändert durch → 404 auf `PUT /zeitplaene/{id}`, obwohl der Zeitplan existiert.

**Wo es bricht**

Der Worker weiss nicht, ob ein Fehler-Result wiederholt werden soll: Entweder wird Unit C endlos wiederholt (Poison Message, blockiert FIFO aus L7) oder Unit D wird nach einer Exception verworfen. Konflikte (L5) haben keinen Fehlerfall und werden zu 500.

**Fix (Verschärfung AD-10)**

> **Ergänzung AD-10:** Für Nicht-HTTP-Aufrufer gilt: Exception → Retry mit Backoff (Kernel); Fehler-Result → endgültig, geloggt, Eintrag als fehlgeschlagen markiert, kein Retry. Ein verworfenes Duplikat ist `Success`. Fehler-Results aus Contract-Commands werden vom Aufrufer auf eigene Fehler abgebildet, nicht ungeprüft durchgereicht. Die Union wird um `ConflictError` (→ 409) erweitert, falls L5 Konflikte als erwartete Fehler behandelt.

---

## L15 — Fremde Daten lesen: Contracts ohne Queries (medium)

**Szenario**

Steuerung braucht für P7/P8 den aktuellen Zustand des Klimageräts. AD-2 erlaubt in Contracts nur Integration Events und Commands.

- **Unit A – Steuerung:** Baut aus `MessungAufgenommen`-Events eine eigene Projektion „letzter Zustand“ in ihrem Schema.
- **Unit B – Steuerung, anderer Slice:** Definiert in `Klimageraet.Contracts` den Command `AktuellenZustandLiefern`, der als Command durch Validation und Transaction läuft und einer offenen Transaktion beitritt.

**Wo es bricht**

Zwei Quellen für denselben Sachverhalt innerhalb von Steuerung mit unterschiedlicher Aktualität. Unit B verletzt den Geist von CQRS (Abfrage mit Transaktion und Schreibrecht) und erzeugt bei L3 Reentrancy-Risiken, ohne eine AD zu brechen.

**Fix (Verschärfung AD-2 und AD-3)**

> **Ergänzung AD-2/AD-3:** Entweder erlaubt `<Modul>.Contracts` ausdrücklich **Queries** (laufen ohne Transaction Behavior, nur lesend, keine Seiteneffekte), oder fremde Daten werden **ausschliesslich** als lokale Projektion aus Integration Events gehalten. Die Spine legt eine der beiden Varianten verbindlich fest; Commands dürfen nie zum Lesen missbraucht werden.

---

## L16 — Veraltete Aufträge an Umsysteme (medium)

**Szenario**

- **Unit A – Steuerung P7:** Erzeugt über Klimageraet den Auftrag „Solltemperatur 23 °C“ für den Zeitabschnitt 14:00–16:00.
- **Worker:** MQTT ist zwei Stunden lang nicht erreichbar; der Auftrag wird at-least-once mit Retry zugestellt (Deferred: Retry-Regeln im Worker).

**Wo es bricht**

Um 16:30 wird der Auftrag doch noch zugestellt und schaltet das Gerät ausserhalb jedes Zeitabschnitts ein. Gleiches gilt für Telegram „Klimagerät offline“, das Stunden nach „wieder online“ ankommt.

**Fix (Ergänzung AD-8)**

> **Ergänzung AD-8:** Jeder Auftrag an ein Umsystem trägt ein `GueltigBis`. Der Worker verwirft abgelaufene Aufträge (geloggt, als verworfen markiert). Gerätebefehle aus Zeit-Triggern sind höchstens bis zum Ende ihres Zeitabschnitts gültig, manuelle Befehle wenige Minuten.

---

## L17 — Format und Präzision des Zeitpunkts der Abfrage (medium)

**Szenario**

- **Unit A – MQTT-Adapter:** Parst den Zeitstempel des Hilfsdiensts `2026-11-11T14:03:00` (ohne Offset) als Ortszeit und wandelt ihn nach UTC.
- **Unit B – Fake im Test / späterer Adapter:** Parst denselben String als UTC.
- Duplikat-Erkennung (Spec P2) vergleicht `DateTimeOffset` im Aggregate (Ticks) mit dem aus der Datenbank geladenen Wert (Mikrosekunden).

**Wo es bricht**

Messungen sind je nach Adapter um eine bzw. zwei Stunden verschoben; nach einem Wechsel des Adapters gelten alle neuen Messungen als „älter als die letzte bekannte“ und werden verworfen. Ein Wert mit Sub-Mikrosekunden-Anteil gilt im Speicher als neuer, nach dem Neuladen als gleich – ein Duplikat wird nach Neustart anders behandelt als davor.

**Fix (Ergänzung AD-11)**

> **Ergänzung AD-11:** Eingehende Zeitpunkte von Umsystemen müssen ISO 8601 mit expliziter Zeitzone sein; Werte ohne Offset werden abgewiesen. Zeitpunkte werden beim Eintritt ins Backend auf Mikrosekunden (Präzision von `timestamptz`) gekürzt und in UTC normalisiert.

---

## L18 — Interceptor-Schleife und Change Detection (low)

**Szenario**

- **Unit A – Domain-Event-Handler:** Fügt ein neues Entity per `Add` hinzu (z. B. Outbox-Eintrag).
- **Unit B – Domain-Event-Handler:** Ändert nur eine Eigenschaft eines bereits geladenen Aggregates, das daraufhin ein neues Domain Event erzeugt.

**Wo es bricht**

Ob die Änderung von Unit B im laufenden `SaveChanges` erfasst und ihr Event in der nächsten Runde der Schleife eingesammelt wird, hängt davon ab, wann die Change Detection relativ zum `SavingChanges`-Hook läuft und woher der Interceptor die Aggregates mit Events bezieht (Change Tracker vs. eigene Registrierung). Ein Event aus einem nur indirekt geänderten Aggregate kann verloren gehen.

**Fix (Ergänzung AD-6)**

> **Ergänzung AD-6:** Der Interceptor ruft vor jeder Runde `ChangeTracker.DetectChanges()` auf und sammelt Events aus allen verfolgten Aggregates; die Schleife hat eine feste Obergrenze an Runden (Exception bei Überschreitung, Schutz vor Endlosschleifen).

---

## L19 — Pflege der Read Models (low)

**Szenario**

- **Unit A – P2 Query „Aktueller Zustand“:** Liest die neueste Zeile aus der Verlaufstabelle.
- **Unit B – P3/P7 Slice:** Pflegt eine eigene Tabelle `aktuelle_zustaende` per Domain-Event-Handler und liest daraus.

**Wo es bricht**

Zwei Wahrheiten für „aktuell“ innerhalb desselben Moduls; nach einer verworfenen älteren Messung oder einem Fehler im Handler laufen sie auseinander.

**Fix (Ergänzung AD-4)**

> **Ergänzung AD-4:** Jedes Read Model hat genau eine Quelle und genau einen Ort der Pflege; ein Read Model, das per Domain-Event-Handler nachgeführt wird, ist die einzige Quelle für Queries zu diesem Sachverhalt.

---

## Empfohlene Reihenfolge

1. Vor dem ersten Modulübergriff (P3/P4): L1, L2, L3, L4, L12, L13 – sie betreffen den gemeinsamen Transaktions- und Zustellmechanismus im `Kernel` und sind später nur mit Umbau aller Slices zu ändern.
2. Vor P2-Produktivbetrieb: L5, L10, L17.
3. Vor P5–P8: L6, L7, L8, L9, L14, L15, L16.
4. Laufend: L11, L18, L19.
