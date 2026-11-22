---
type: architecture-review
reviews: ../ARCHITECTURE-SPINE.md
perspective: adversarial (Gegner), Update 2026-11-21
created: '2026-11-21'
context: SPEC.md (CAP-9 bis CAP-15), prozesse.md, CONTEXT.md, ADR 0004, Spec P2
---

# Adversarieller Review, Update 2026-11-21 – Modul Zuhause, AD-18, AD-19

## Methode

Wie im Review vom 2026-11-11: Für jedes Loch werden zwei Einheiten eine Ebene unter der Spine konstruiert (Slices, Adapter, Worker, Entwickler/Agenten), die **jede AD buchstabengetreu einhalten** und trotzdem nicht zusammenpassen. Jeder Befund zitiert die Stellen und schlägt eine neue oder verschärfte AD vor. Geprüft werden nur die neuen und geänderten Teile (AD-1, 8, 9, 11, 14, 15, 17, 18, 19, Modul Zuhause). Nummerierung `Z1…`, damit sie nicht mit `L1…L19` des früheren Reviews kollidiert.

Beobachtung vorab: Mehrere Fixes des früheren Reviews sind **nicht** oder nur halb in die Spine gewandert und schlagen jetzt in Zuhause voll durch (kein `GueltigBis` für Aufträge an Umsysteme, keine Semantik Fehler-Result vs. Exception für Nicht-HTTP-Aufrufer (L14), kein fachlicher Schlüssel in AD-12 (L10), keine Regel für Reads über Modulgrenzen (L15), kein Retry für Nicht-Outbox-Einstiege (L5)). Sie sind unten nur dort wiederholt, wo Zuhause einen neuen, konkreten Schaden daraus macht.

## Gesamturteil

Die Spine ist für Zuhause **nicht baufertig**. Das Aggregate `Anwesenheit` hat drei Commands und zwei Domain Events, aber keine Zustände, keine Übergänge und keinen Treiber für die 2-Minuten-Frist; drei verschiedene Einstiege (Zeit-Trigger, HA-Ereignisse) schreiben parallel auf dieselbe Zeile ohne Wiederholungsregel; die Selbstzustellung der modulinternen Events steht im Widerspruch zu AD-8; und das Verhalten bei Home-Assistant-Ausfall ist nach "Deferred" verschoben, obwohl P9 bereits davon abhängt. Zwei critical, sieben high, vier medium, ein low.

## Übersicht

| # | Loch | Schwere | Betroffene AD |
| --- | --- | --- | --- |
| Z1 | Anwesenheit: keine Zustandsmaschine, kein Treiber für die 2-Minuten-Frist | critical | AD-11, AD-15, AD-19 |
| Z2 | Parallele Einstiege auf dasselbe Aggregate, Konflikt ohne Wiederholung (verlorene Bewegung) | critical | AD-10, AD-15, AD-16, AD-19 |
| Z3 | Modulinterne Events: Worker stellt nur an "andere Module" zu | high | AD-8, AD-19 |
| Z4 | 50-W-Regel: Zuordnung, Alter, Schwelle, Phase "bereits abwesend" | high | AD-18, AD-19 |
| Z5 | "Erste Bewegung nach Rückkehr der IP": zwei Zeitquellen, Reihenfolge, Bewegung vor IP | high | AD-8, AD-11, AD-19 |
| Z6 | Home-Assistant-Ausfall und Reconnect: Fehlabwesenheit, Phantom-Bewegung, eingefrorene Leistung | high | AD-18, AD-19, Deferred |
| Z7 | Gemeinsame Outbox-Queue: HA-Ausfall blockiert Fachereignisse, veraltete Aufträge, Poison | high | AD-8, AD-10 |
| Z8 | Einrichten/Ausschalten: Teilfehler, Befehlsgranularität, Entscheid auf Verarbeitungszeit | high | AD-8, AD-9, AD-14, AD-19 |
| Z9 | Besitz der letzten Raum-/Aussentemperatur, "zuverlässig", "Status holen" | high | AD-3, AD-4, AD-17, AD-19 |
| Z10 | Zielzustand an Steckdosen gegen Zustand, der in Home Assistant wechselt; Echo-Schleife | medium | AD-9, AD-18, AD-19 |
| Z11 | Anwesenheit wird von mehreren Einstiegen implizit angelegt | medium | AD-12, AD-19 |
| Z12 | Meteomatics (P12) liegt in keiner Zugriffsregel | medium | AD-8, AD-9, AD-18 |
| Z13 | Zwei Home-Assistant-Adapter, weil der HA-Vertrag erst "vor P10" kommt | medium | AD-18, Deferred |
| Z14 | Status-Etiketten uneinheitlich: AD-18 "vorgeschlagen", AD-15 bis 19 ohne Etikett | low | alle |

---

## Z1 — Anwesenheit: keine Zustandsmaschine, kein Treiber für die Frist (critical)

**Zitate**

- AD-19: "Das Aggregate `Anwesenheit` ändert sich nur durch die Commands `IpPraesenzAufnehmen`, `BewegungAufnehmen` und `LeistungAufnehmen`. Daraus entstehen die Domain Events `NachhauseGekommen` und `AbwesendGeworden`".
- AD-15: "Zeit-Trigger (z. B. Prüfung der IP-Präsenz)" … "sendet genau einen Command bzw. eine Query … er enthält keine Fachlogik."
- AD-11: "Fristen (Offline und Abwesend je 2 Minuten) rechnen auf UTC-Zeitpunkten."
- SPEC CAP-9: "2 Minuten lang nicht mehr … antwortet"; Open Question: "Zieht der Schreibtisch mehr als 50 W, während der Bewohner schon abwesend ist, endet die Abwesenheit dann auch".
- CONTEXT.md: "Eine Abwesenheit endet erst mit dem Nachhause-Kommen."

**Szenario**

- **Einheit A – Slice IP-Prüfung:** Der Zeit-Trigger prüft alle 30 s und sendet bei **jeder** Prüfung `IpPraesenzAufnehmen(Antwortet, Zeitpunkt)`. Das Aggregate merkt sich `LetzteAntwort` und wird bei `Antwortet=false` und `Zeitpunkt - LetzteAntwort >= 2 min` abwesend. Frist im Aggregate.
- **Einheit B – Slice IP-Prüfung eines anderen Entwicklers:** Der Trigger sendet `IpPraesenzAufnehmen` **nur bei Flanken** (war erreichbar, jetzt nicht) und hält die 2 Minuten selbst mit einem Timer; erst danach sendet er `IpPraesenzAufnehmen(Antwortet=false)`, und das Aggregate setzt sofort abwesend. Das verstösst gegen "keine Fachlogik" im Einstieg, aber AD-19 sagt nirgends, wo die Frist wohnt, und AD-11 sagt nur, womit gerechnet wird.
- **Einheit C – Slice Leistung:** `LeistungAufnehmen` speichert nur den letzten Wert; die Auswertung erfolgt beim nächsten IP-Command. Einheit D wertet bei jedem `LeistungAufnehmen` sofort aus und beendet bei Leistung < 50 W die Wartezeit.

**Wo es bricht**

- A und B liefern bei demselben Ablauf verschiedene Zeitpunkte für `AbwesendGeworden` (Fristablauf vs. Verarbeitungszeitpunkt), und mit B fehlt dem Aggregate jede Möglichkeit, "2 Minuten verstrichen" zu erfahren, wenn der Trigger zwischen zwei Flanken schweigt. Ob "IP kehrt zurück und fällt wieder weg, bevor die Bewegung kam" (WLAN-Flackern) den Merker `IpZurueck` löscht, steht nirgends: A löscht, B nicht; in B löst die nächste Bewegung ein Nachhause-Kommen aus, in A nicht.
- Ohne Zustandsmaschine kann kein Seam-Test (Spec: "Fakes für IP-Prüfung und Steckdose") die Ablaufreihenfolge eindeutig prüfen; die Tests zweier Agenten widersprechen sich.

**Fix (neue Rule, Ergänzung AD-19)**

> **AD-19, Ergänzung:** `Anwesenheit` ist eine explizite Zustandsmaschine mit den Zuständen `Anwesend`, `Abwesend(IpZurueck: bool)` und den **einzigen** erlaubten Übergängen: (1) `Anwesend → Abwesend`, wenn seit der letzten IP-Antwort ≥ 2 min vergangen sind **und** keine Leistung > 50 W an Steckdose 2 gilt (Z4); (2) `Abwesend(false) → Abwesend(true)` bei IP-Antwort; (3) `Abwesend(true) → Abwesend(false)`, wenn die IP erneut ausbleibt; (4) `Abwesend(true) → Anwesend` bei Bewegung (Erzeugung von `NachhauseGekommen`); alle anderen Kombinationen (Bewegung bei `Anwesend`, Bewegung bei `Abwesend(false)`, Leistung in `Abwesend`) sind No-ops ohne Event, ausdrücklich keine Fehler. Die **Frist wertet ausschliesslich das Aggregate** aus, aus `LetzteIpAntwort` und dem fachlichen Zeitpunkt des Commands. Der Zeit-Trigger sendet bei **jeder** Prüfung `IpPraesenzAufnehmen(Antwortet, Zeitpunkt)` (Zustand, keine Flanke), unabhängig vom Ergebnis. `AbwesendGeworden` trägt als fachlichen Zeitpunkt `LetzteIpAntwort + 2 min`, nicht den Verarbeitungszeitpunkt.

---

## Z2 — Parallele Einstiege auf dasselbe Aggregate, Konflikt ohne Wiederholung (critical)

**Zitate**

- AD-16: "Jedes Aggregate hat ein Concurrency Token (PostgreSQL `xmin`). Ein Konflikt ist eine Exception (AD-10); Outbox-Konsumenten werden vom Worker erneut zugestellt."
- AD-10: "Alles Unerwartete, auch transiente Fehler, ist eine Exception und landet beim globalen Exception Handler (500)."
- AD-15: "Jeder Einstiegspunkt erzeugt einen eigenen DI-Scope und sendet genau einen Command".
- AD-18: "Eingehende Zustandsänderungen (Bewegung am Eingang, Leistung der Steckdosen) werden zu Commands von Zuhause".

**Szenario**

- **Einheit A – HA-Adapter, Leistung:** Der WebSocket-Handler verarbeitet jedes `state_changed` in einem eigenen Task und eigenem Scope: `LeistungAufnehmen`. Die Steckdose meldet bei laufendem Rechner alle paar Sekunden.
- **Einheit B – HA-Adapter, Bewegung:** Verarbeitet das Bewegungsereignis parallel: `BewegungAufnehmen`.
- **Einheit C – Zeit-Trigger IP:** Sendet `IpPraesenzAufnehmen`.
- Alle drei schreiben dieselbe `Anwesenheit`-Zeile (ein Nutzer, ein Aggregate).

**Wo es bricht**

- Zwei Commands laden dieselbe Version; der zweite `SaveChanges` wirft `DbUpdateConcurrencyException`. Für Outbox-Konsumenten gibt es Redelivery, für HA-Ereignisse und Zeit-Trigger **keine Regel**: A, B und C sind regelkonform, wenn sie die Exception loggen und weitermachen. Die **erste Bewegung am Eingang** (die einzige, die `NachhauseGekommen` auslösen darf) kann so von einer gleichzeitigen `LeistungAufnehmen` verdrängt werden und geht verloren; die nächste Bewegung rettet es nur, wenn sie noch kommt. Umgekehrt kann ein verlorenes `IpPraesenzAufnehmen` das Abwesend-Werden verzögern.
- Die Hochfrequenz der Leistungsmeldungen erzeugt Dauerkonflikte (die Zeile wird bei jeder Leistungsänderung neu geschrieben) und lässt jedes Mal einen 500er im Exception Handler zurück, der für HTTP gedacht ist.

**Fix (Ergänzung AD-16 und AD-15)**

> **AD-16, Ergänzung:** Jeder Einstieg, der nicht über die Outbox kommt (HA-Ereignis, Zeit-Trigger, MQTT-Eingang), wiederholt seinen Command bei `DbUpdateConcurrencyException` mit frischem Scope, höchstens 3-mal mit kurzem Backoff, und loggt danach als Fehler. Ereignisse eines Einstiegs für dasselbe Aggregate werden **sequenziell** verarbeitet (ein Kanal pro Aggregate), damit die Reihenfolge der Quelle erhalten bleibt.
> **AD-19, Ergänzung:** Nur Zustandsänderungen, die das Aggregate tatsächlich ändert, werden geschrieben. `LeistungAufnehmen` schreibt das Aggregate nur, wenn die Überschreitung der 50-W-Schwelle ihren Wahrheitswert ändert, nicht bei jedem Messwert.

---

## Z3 — Modulinterne Events: der Worker stellt nur an "andere Module" zu (high)

**Zitate**

- AD-8: "Ein Background Worker stellt sie nach dem Commit zu – Integration Events über den Mediator an die Handler **anderer Module**".
- AD-19: "`Einrichten` und `Ausschalten` laufen als Outbox-Konsumenten dieser Events … Die Events sind modulintern; `Zuhause.Contracts` entsteht erst, wenn ein anderes Modul sie braucht."
- AD-2: Contracts enthalten Integration Events; AD-8: Konsument übersetzt "ein Integration Event in genau einen Command des eigenen Moduls".

**Szenario**

- **Einheit A – Worker (Kernel):** Baut die Zustellung nach AD-8 wörtlich: Er sucht Handler für einen Outbox-Eintrag in den **Contracts-Assemblies fremder Module**. `NachhauseGekommen` liegt nicht in `Zuhause.Contracts`, also findet der Worker keinen Konsumenten; der Eintrag bleibt für immer unzugestellt oder wird als "ohne Konsument" abgehakt.
- **Einheit B – Zuhause, Slice Einrichten:** Registriert den Konsumenten im eigenen Modul, wie AD-19 es verlangt, und nimmt an, der Worker liefere zu.

**Wo es bricht**

Beides ist wörtlich korrekt; die Hauptfunktion der Wohnung (Einrichten, Ausschalten) wird nie ausgeführt. Dazu kommt die Typ-Auflösung (Typname oder Attribut, früherer Review L11) für Events, die weder in Contracts noch als Integration-Event-Typ definiert sind: Wo liegt der Typ, auf den die Outbox deserialisiert?

**Fix (Verschärfung AD-8 und AD-19)**

> **AD-8, Ergänzung:** Der Worker stellt Integration Events an **alle registrierten Konsumenten** zu, auch im eigenen Modul des Produzenten. Ein Integration Event liegt in `<Modul>.Contracts`, sobald es einen Konsumenten ausserhalb des Moduls hat, sonst in `<Modul>/Infrastructure/IntegrationEvents/`; beide haben einen expliziten, stabilen Typnamen (Attribut), der in der Outbox gespeichert wird.
> **AD-19, Ergänzung:** `NachhauseGekommen` und `AbwesendGeworden` sind als Integration Events nur Zuhause-intern; ihre Konsumenten `Einrichten` und `Ausschalten` sind je genau ein Konsument mit eigener Inbox-Kennung.

---

## Z4 — 50-W-Regel: Zuordnung, Alter, Schwelle, Phase "bereits abwesend" (high)

**Zitate**

- CAP-9: "Zieht der Schreibtisch (Steckdose 2) mehr als 50 W, gilt der Bewohner auch ohne IP als anwesend."
- AD-18: "Geräte und Entity-IDs sind Konfiguration (Options-Pattern)"; "n Steckdosen" (CAP-10); AD-19: `LeistungAufnehmen`.
- SPEC Open Question: "… während der Bewohner schon abwesend ist, endet die Abwesenheit dann auch, oder gilt die Regel nur vor dem Abwesend-Werden?"
- CONTEXT.md **Anwesend**: "Eine Abwesenheit endet erst mit dem Nachhause-Kommen."

**Szenario**

- **Einheit A – HA-Adapter:** Filtert über die Konfiguration nur Steckdose 2 und sendet `LeistungAufnehmen(Watt)`; das Aggregate kennt keine Steckdosen-ID. (Entscheid im Adapter: ohne Fachlogik im Einstieg unvereinbar, aber die Zuordnung "Steckdose 2 = Schreibtisch" ist Konfiguration, nicht Domäne, laut AD-18.)
- **Einheit B – Adapter eines zweiten Slices (P10, "Leistung abrufbar", CAP-10):** Sendet `LeistungAufnehmen(SteckdoseId, Watt)` für alle Steckdosen; das Aggregate wertet jedes `Watt > 50` als Anwesenheit, auch Steckdose 1 (Licht, z. B. mit LED-Treiber/Verlängerung).
- **Aggregate A:** Beendet bei `Watt > 50` in `Abwesend` die Abwesenheit (Regel "gilt als anwesend"). **Aggregate B:** ignoriert in `Abwesend` jede Leistung (CONTEXT: endet nur mit Nachhause-Kommen).
- **Auswertung:** A hält einen Leistungswert ohne Zeitpunkt; B speichert Zeitpunkt und verwirft Werte, die älter als X sind. Die Spine nennt weder Alter noch Hysterese.

**Wo es bricht**

- Nachhause-Kommen-Pfad: In A endet die Abwesenheit durch einen verspäteten 60-W-Wert, **ohne** `NachhauseGekommen`, also ohne Einrichten, und die Anwesenheit wird danach durch die ausbleibende IP erneut abwesend → erneutes `Ausschalten` (Schleife; siehe auch Z10). In B nicht.
- Standby-Rechner um 40 bis 80 W: Bei A kippt die Anwesenheit mit jedem Wert; B wartet. Ein vor dem Ausfall von Home Assistant gemeldeter Wert > 50 W hält die Anwesenheit ewig (Z6).
- "Genau einmal pro Abwesenheit" (CAP-9 success) lässt sich mit A nicht erfüllen, wenn die Leistung während der 2 Minuten flackert.

**Fix (Ergänzung AD-19 und AD-18)**

> **AD-19, Ergänzung:** `LeistungAufnehmen` trägt `SteckdoseId`, `Watt` und den fachlichen Zeitpunkt; die Konfiguration bestimmt, welche Steckdose die Rolle "Schreibtisch" hat (`Options`), das Aggregate wertet nur diese. Ein Leistungswert > 50 W gilt als **Beleg für Anwesenheit nur im Zustand `Anwesend`** und setzt die Frist zurück (`LetzteIpAntwort` wird durch `LetzterAnwesenheitsbeleg = max(IP-Antwort, Zeitpunkt des Werts > 50 W)` ersetzt); in `Abwesend` ist Leistung ein No-op (Abwesenheit endet nur durch `NachhauseGekommen`). Ein Leistungswert ist höchstens 5 Minuten gültig; ältere Werte zählen als "nicht > 50 W".

---

## Z5 — "Erste Bewegung nach Rückkehr der IP": zwei Zeitquellen, Reihenfolge (high)

**Zitate**

- CONTEXT.md **Nachhause-Kommen**: "Die erste Bewegung des Bewegungsmelders nach einer Abwesenheit, nachdem die IP ins Netzwerk zurückgekehrt ist. … Die Rückkehr der IP allein ist noch kein Nachhause-Kommen."
- AD-8: "den fachlichen Zeitpunkt, auf den sich Konsumenten stützen – nie auf den Zeitpunkt der Verarbeitung."
- AD-11: "Zeit kommt nur aus `TimeProvider`."
- SPEC Open Question: "Was gilt bei einer Bewegung, bevor die IP zurück ist (z. B. WLAN am Telefon aus)?"; Deferred: "Event-Form" des HA-Vertrags.

**Szenario**

- **Einheit A – HA-Adapter:** Setzt den Zeitpunkt der Bewegung aus HA (`time_fired`/`last_changed`) und des IP-Triggers aus dem `TimeProvider`. Das Aggregate entscheidet über "nach" per **Zeitpunkt-Vergleich**.
- **Einheit B – HA-Adapter eines anderen Slices:** Setzt `TimeProvider.GetUtcNow()` beim Eintreffen; Aggregate entscheidet nach **Eintreffreihenfolge**.

**Wo es bricht**

- Die IP-Prüfung läuft im Takt (z. B. 30 s). Das Telefon verbindet sich, während der Bewohner die Tür öffnet; der Melder meldet um 18:00:05, die Prüfung bemerkt die IP um 18:00:25 und stempelt 18:00:25. Nach A liegt die Bewegung **vor** der Rückkehr und wird verworfen (die Bewegung ist die einzige in den nächsten Minuten; kein Einrichten); nach B passt es nicht, wenn die Prüfung die Bewegung überholt.
- "Bewegung vor IP": A verwirft, B merkt sich die Bewegung als offen und löst bei IP-Rückkehr rückwirkend aus. Die Spine regelt das nicht, die SPEC hat es als offene Frage.
- Duplikate/älter: Spec P2 verwirft ältere Messungen; für `Anwesenheit` gibt es keine entsprechende Regel (HA-Reconnect kann Ereignisse doppelt liefern, Z6).

**Fix (Ergänzung AD-11 und AD-19)**

> **AD-11, Ergänzung:** Der fachliche Zeitpunkt eines Gerätereignisses ist der Zeitstempel des Umsystems (HA `time_fired`), normalisiert auf UTC und Mikrosekunden; Fehlt er, der `TimeProvider`-Zeitpunkt beim Eintritt, nie später. Der Zeitpunkt des IP-Ergebnisses ist der **Beginn** der Prüfung.
> **AD-19, Ergänzung:** "Nach der Rückkehr der IP" wird über **Zeitpunkte** entschieden, mit einer Toleranz von 60 s zugunsten der Bewegung (Bewegung ≥ `IpZurueckSeit − 60 s`). Eine Bewegung im Zustand `Abwesend(false)` wird **verworfen** (No-op, Z1). Commands mit Zeitpunkt ≤ dem zuletzt verarbeiteten Zeitpunkt desselben Quelltyps werden verworfen wie in Spec P2.

---

## Z6 — Home-Assistant-Ausfall und Reconnect (high)

**Zitate**

- Deferred: "**Home-Assistant-Vertrag** (Authentifizierung, Reconnect, Event-Form, Verhalten bei Ausfall) … eigenes Slice vor P10."
- Deferred: "**Prüfverfahren der IP-Präsenz** (Ping/ARP oder Home Assistant): mit … P9 … entscheiden."
- ADR 0004: "Fällt er aus, fallen Bewegung und Schalten aus. Das Backend muss damit umgehen (Reconnect, Verhalten bei Ausfall)."
- Capability Map: P9 → AD-18; AD-18: "(Leistung der Steckdosen) werden zu Commands von Zuhause".

**Szenario**

- **Einheit A – P9 (vor P10 gebaut):** Baut `LeistungAufnehmen` und die IP-Prüfung via `device_tracker` aus HA (eine zulässige Variante von "Ping/ARP oder Home Assistant"). Nach dem Reconnect abonniert sie nur neu, ohne Zustandsabgleich.
- **Einheit B – P10 (Bewegung):** Nach dem Reconnect ruft sie `get_states` ab und wandelt jeden `binary_sensor` in "on" in ein `BewegungAufnehmen`.

**Wo es bricht**

- HA ist 5 Minuten weg (Update, Neustart des Containers). A: `device_tracker` meldet "unbekannt", die Prüfung wertet dies als "antwortet nicht": Nach 2 Minuten ist der Bewohner **abwesend**, `Ausschalten` schaltet Klimagerät (über MQTT, funktioniert) und gleichzeitig Steckdosen aus (Outbox-Aufträge an das ausgefallene HA, bleiben liegen). HA kommt zurück: IP wieder da, eine Bewegung im Wohnzimmer-Melder → `NachhauseGekommen`, obwohl der Bewohner nie weg war.
- Nach dem Reconnect liefert B den Zustand "on" des Melders aus dem Abgleich als frische Bewegung (Zeitpunkt = jetzt), A hat ihn nie gesehen. Bei B wird bei `Abwesend(true)` ein Phantom-Nachhause-Kommen ausgelöst.
- Während des Ausfalls kommt kein Leistungswert > 50 W; der letzte Wert (z. B. 60 W) bleibt stehen. Der Schreibtisch ist längst aus; die Anwesenheit hält ewig (Z4).
- P9 baut vor dem HA-Slice sein eigenes Gateway (Z13).

**Fix (Neue Rule AD-20 "Home-Assistant-Anbindung", vor P9 festzulegen, nicht vor P10)**

> **AD-20 — Home-Assistant-Vertrag:** Der Adapter liefert zusätzlich zu Ereignissen einen Verbindungszustand `Verbunden/Getrennt` und keine Ereignisse im Zustand `Getrennt`. Nach jedem (Wieder-)Verbinden gleicht er genau einmal den Zustand aller konfigurierten Entitäten ab (`get_states`); abgeglichene Werte werden **nie** als Bewegung gemeldet, sondern nur als `LeistungAufnehmen` mit dem Zeitstempel der Entität. Solange der Adapter `Getrennt` ist, gilt jeder Leistungswert als veraltet (Z4), **und die IP-Prüfung über HA liefert "unbekannt", das nie zu `Abwesend` führt**: Die Anwesenheit wird nur aus einer **eigenen** IP-Prüfung (Ping/ARP vom Backend) bestimmt, nicht über HA. Das Aggregate unterscheidet `Antwortet`, `Antwortet nicht` und `Unbekannt`; `Unbekannt` verlängert die Frist nicht und verkürzt sie nicht.

---

## Z7 — Gemeinsame Outbox-Queue: HA-Ausfall blockiert Fachereignisse (high)

**Zitate**

- AD-8: "Integration Events und Aufträge an Umsysteme werden in … die Outbox-Tabelle des eigenen Modul-Schemas geschrieben. … Die Zustellung ist **at-least-once**, FIFO pro Modul und sequenziell."
- AD-8: "Aufrufe an Umsysteme (Telegram, Home Assistant) bleiben at-least-once".
- AD-10: keine Regel, was ein Konsument oder der Worker bei einem Fehler-Result tut.
- Deferred: "Retry-, Backoff- und Aufräumregeln der Outbox: … Festlegung mit dem ersten Worker."

**Szenario**

- **Einheit A – Worker:** Eine Queue pro Modul, FIFO, sequenziell, ein fehlschlagender Eintrag blockiert die nachfolgenden. In der Outbox von Zuhause liegen **Aufträge an HA** ("Steckdose 2 ein") **und** die Integration Events `NachhauseGekommen`/`AbwesendGeworden` gemischt.
- **Einheit B – Einrichten:** Schreibt die Steckdosen-Aufträge in dieselbe Outbox und rechnet damit, dass sie bald hinausgehen.

**Wo es bricht**

- HA ist weg: Der erste Steckdosen-Auftrag blockiert die Queue. `AbwesendGeworden` (Ausschalten) liegt dahinter und wird nicht verarbeitet, das **Klimagerät** (Contract-Command, geht über Klimageraet, hätte funktioniert) bleibt an. Wird HA wieder erreichbar, laufen alle angestauten Aufträge in der alten Reihenfolge; ein Auftrag "Licht ein" von vor zwei Stunden schaltet das Licht ein, obwohl der Bewohner inzwischen ausgegangen ist und die zweite Hälfte (aus) noch folgt: kurzes Flackern, aber bei manuell geschalteten Steckdosen (Z10) bleibt ein veralteter Befehl stehen.
- Ein Fehler-Result im Konsumenten (z. B. `ValidationError` aus dem Klimagerät) wird vom Worker nach AD-8/AD-10 entweder endlos wiederholt (blockiert alle Folgeevents) oder fallengelassen (verlorenes Einrichten). Das ist L14 und L16 des früheren Reviews, jetzt mit Schaden.

**Fix (Verschärfung AD-8 und AD-10, wie L14 und L16, jetzt zwingend)**

> **AD-8, Ergänzung:** Aufträge an Umsysteme und Integration Events liegen in **getrennten Queues** desselben Schemas (`outbox_events`, `outbox_auftraege`); FIFO gilt pro Queue und pro Ziel (Telegram, MQTT, Home Assistant), sodass ein Umsystem-Ausfall keine Fachereignisse blockiert. Jeder Auftrag trägt `GueltigBis`; der Worker verwirft abgelaufene Aufträge (geloggt, als verworfen markiert). Gerätebefehle aus Einrichten/Ausschalten gelten höchstens 5 Minuten.
> **AD-10, Ergänzung:** Für Nicht-HTTP-Aufrufer gilt: Exception → Retry mit Backoff und Obergrenze; Fehler-Result → endgültig, geloggt, Eintrag als fehlgeschlagen markiert, **kein** Retry und keine Blockade der Folgeeinträge.

---

## Z8 — Einrichten/Ausschalten: Teilfehler, Befehlsgranularität, Entscheid auf Verarbeitungszeit (high)

**Zitate**

- AD-19: "`Einrichten` und `Ausschalten` laufen als Outbox-Konsumenten … schalten Steckdosen über die Outbox (AD-18) und senden das Klimagerät über Commands aus `Klimageraet.Contracts` (AD-14)".
- AD-8: "Ein Konsument übersetzt ein Integration Event in genau einen Command des eigenen Moduls."
- AD-14: "Ein beigetretenes Behavior setzt vor dem Handler einen Savepoint und rollt bei Fehler-Result oder Exception darauf zurück." / "Nur das äusserste Transaction Behavior committet".
- AD-9: "Befehle beschreiben immer einen Zielzustand (z. B. „Solltemperatur 22 °C“)".
- CAP-14: "Eingeschaltet wird immer mit Modus Kühlen, iECO an und Solltemperatur 22 °C."

**Szenario**

- **Einheit A – Einrichten:** Ein Command `Einrichten`; der Handler ruft nacheinander `Klimageraet.Contracts.ModusSetzen`, `EcoSetzen`, `SolltemperaturSetzen`, `Einschalten` auf (vier Contract-Commands, vier MQTT-Aufträge). Das Klimagerät wirft ein `ValidationError` ("iECO nicht unterstützt", CAP-1). A gibt das Fehler-Result weiter: Savepoint, Rollback; auch die Steckdosen-Aufträge, der Inbox-Eintrag und alles andere werden zurückgerollt; der Worker wiederholt oder verwirft (Z7).
- **Einheit B – Einrichten:** Ein Contract-Command `EinschaltenMitVorgaben(Modus, Eco, Solltemperatur)` (ein Zielzustand), Steckdosen werden zuerst und unabhängig geschrieben; ein Fehler-Result des Klimageräts wird geloggt und `Success` zurückgegeben: Desk und Licht gehen an, Klima nicht, kein Hinweis.
- **Entscheid "warm" und "dunkel":** A liest `Dunkel` und die Raumtemperatur beim Verarbeiten (`TimeProvider`), B liest zum fachlichen Zeitpunkt aus `NachhauseGekommen.Zeitpunkt`.

**Wo es bricht**

- Teilwirkung (A: gar nichts; B: Steckdosen ohne Klima) ohne dass die Spine festlegt, was "Einrichten" fachlich ist: alles oder nichts, oder jede Wirkung für sich.
- Vier einzelne Zielzustände ergeben vier MQTT-Nachrichten, die das Gerät einzeln anwendet (Solltemperatur 22 °C wird im Modus Heizen wirkungslos). Wie viele Contract-Commands "ein Zielzustand" ist, bestimmt der Implementierer.
- Nach HA-Ausfall (Z7) läuft ein altes `NachhauseGekommen` Stunden später mit **aktuellem** `Dunkel`/Temperatur in Einrichten; die Schreibtisch-Wirkung ist spät, das Klima-Entscheid falsch.

**Fix (Ergänzung AD-19 und AD-9)**

> **AD-19, Ergänzung:** `Einrichten` und `Ausschalten` sind je **ein** Command, der **alle** Wirkungen unabhängig zusammen schreibt (Steckdosen-Aufträge, Contract-Command ans Klimagerät): schlägt der Contract-Command mit einem Fehler-Result fehl, wird dies **geloggt und zählt als Wirkung "nicht ausgeführt"**, der Command liefert `Success` und committet die übrigen Wirkungen. Entscheidungen (`Dunkel`, `Warm`) stützen sich auf den fachlichen Zeitpunkt des auslösenden Events (Z9). Ist das Event älter als 10 Minuten, wird der Command verworfen (`Success`, geloggt).
> **AD-9, Ergänzung:** Für das Klimagerät gibt es **einen** Contract-Command je fachlichem Zielzustand (z. B. `KlimagerätEinschalten(Modus, Eco, Solltemperatur)`), nie eine Folge von Einzelcommands.

---

## Z9 — Besitz der letzten Raum-/Aussentemperatur in Zuhause (high)

**Zitate**

- AD-17: "Zusätzlich veröffentlicht es mit jeder Messung `MesswerteGemeldet` (Raumtemperatur, Aussentemperatur, fachlicher Zeitpunkt); Zuhause hält daraus den letzten Wert für die Warm-Prüfung (CAP-14)."
- AD-19: "Das Aggregate `Anwesenheit` ändert sich **nur** durch die Commands `IpPraesenzAufnehmen`, `BewegungAufnehmen` und `LeistungAufnehmen`."
- AD-4: "Commands ändern Zustand nur über ein Aggregate."
- AD-3: "Daten anderer Module erhält es ausschliesslich über Integration Events oder synchrone Commands".
- CONTEXT.md **Warm**: "Ist die Raumtemperatur nicht zuverlässig, ist es warm, wenn die Aussentemperatur über 25 °C liegt." SPEC CAP-14: "… und das System holt danach den aktuellen Status des Klimageräts."
- SPEC Open Question: "Wird `MesswerteGemeldet` bei jeder Messung oder nur bei Änderung veröffentlicht, und wie stösst CAP-14 „aktuellen Status holen“ eine frische Messung an?"

**Szenario**

- **Einheit A – Zuhause, Konsument `MesswerteGemeldet`:** Der Command `MesswerteMerken` schreibt Raum- und Aussentemperatur in das Aggregate `Anwesenheit` (ein Aggregate pro Modul, ein Nutzer). Das verletzt AD-19 (nur drei Commands), aber AD-4 verlangt "über ein Aggregate", und die Spine sagt nicht, welches.
- **Einheit B – Zuhause, dieselbe Konsumentenfunktion:** Legt ein eigenes Aggregate `Raumklima` an.
- **Einheit C:** Hält den Wert nur als Projektionstabelle (Read Model) per Domain-Event-Handler und liest sie in `Einrichten`.
- **Warm-Prüfung, Zuverlässigkeit:** A nimmt "zuverlässig = Raumtemperatur ≠ null"; B "Klimagerät ist eingeschaltet" (aus `Eingeschaltet`/`Ausgeschaltet` mit eigenem Zustand); C "Wert nicht älter als 10 Minuten".

**Wo es bricht**

- A schreibt jede Minute dieselbe Zeile, die Bewegung und Leistung zählen (Z2): Konflikt-Zuwachs, Minute für Minute.
- Zwei Besitzer derselben Information: Spec P2 (`MesswerteGemeldet` trägt nur Zahlen und Zeit) enthält weder "ist das Gerät an" noch "Zuverlässigkeit"; B und C bauen unterschiedliche Warm-Entscheide aus denselben Daten. Ist das Klimagerät offline (kein `MesswerteGemeldet`), hält Zuhause einen Wert von vor einer Woche und rechnet damit.
- "Status holen" ist in der Spine nicht modelliert: Zuhause darf nach AD-2/AD-14 nur einen Command senden, die frische Messung kommt aber **nach** dem Commit asynchron. `Einrichten` kann in seiner einen Transaktion nie darauf warten. Wer entscheidet danach über das Klima, wenn die Messung eintrifft? Ein zweiter Konsument von `MesswerteGemeldet` mit einem offenen "Warte auf Messung"-Zustand, der in keinem Aggregate lebt.

**Fix (Ergänzung AD-17, AD-19, AD-3)**

> **AD-17, Ergänzung:** `MesswerteGemeldet` trägt `Raumtemperatur: decimal?` (null = nicht gemessen), `Aussentemperatur: decimal?`, `KlimageraetEingeschaltet: bool` und den fachlichen Zeitpunkt der Messung. Zuverlässigkeit bestimmt Klimageraet, nicht der Konsument: Feld `RaumtemperaturZuverlaessig: bool`.
> **AD-19, Ergänzung:** Zuhause hält den letzten Messwert in einem **eigenen** Aggregate `Raumklima` (nicht in `Anwesenheit`) mit höchstens einem Wert pro Klimagerät. Ein Wert, dessen Zeitpunkt älter als 10 Minuten ist, gilt als nicht zuverlässig. Die Warm-Prüfung in `Einrichten` liest ausschliesslich `Raumklima` und ist eine reine Funktion von `Raumklima`, Konfiguration und fachlichem Zeitpunkt des Nachhause-Kommens.
> **AD-9/AD-2, Ergänzung:** Ein Command "Status anfordern" in `Klimageraet.Contracts` erzeugt nur einen MQTT-Auftrag; das Ergebnis kommt als neues `MesswerteGemeldet`. Das Warten auf die frische Messung ist ein eigener Prozessschritt: `Raumklima` führt ein Feld `KlimaEntscheidOffenSeit`, und ein Konsument von `MesswerteGemeldet` schliesst es ab (Zeitlimit 2 Minuten, danach Entscheid nach Aussentemperatur-Ersatzregel).

---

## Z10 — Zielzustand an Steckdosen gegen Zustand, der in Home Assistant wechselt (medium)

**Zitate**

- AD-18: "Schaltbefehle an Steckdosen sind Outbox-Aufträge mit Zielzustand (AD-8, AD-9)."; eingehend nur "Bewegung am Eingang, Leistung der Steckdosen".
- AD-9: "Befehle beschreiben immer einen Zielzustand …, damit eine doppelte Zustellung nichts ändert. **Der Zustand des Aggregates Klimageraet ändert sich nur durch `MessungAufnehmen`**".
- CAP-15 success: "ein bereits ausgeschaltetes Gerät bleibt unverändert."

**Szenario**

- **Einheit A – Zuhause:** Hält gar keinen Zustand der Steckdose (ein/aus); schickt bei jedem Einrichten "ein" und bei jedem Ausschalten "aus" blind.
- **Einheit B – Zuhause, Slice P10 ("zuletzt gemeldete Leistung abrufbar"):** Führt ein Aggregate `Steckdose` mit Zustand und Leistung, ändert den Zustand beim Absenden des Befehls (die Variante, die AD-9 für das Klimagerät ausdrücklich verbietet; für Steckdosen gibt es keine Aussage).
- **Einheit C – HA-Automation / Schalter von Hand:** Der Bewohner schaltet das Licht von Hand oder über die HA-App.

**Wo es bricht**

- A kann nicht prüfen, ob etwas geändert wurde (CAP-15: "bleibt unverändert"; nur über Zielzustand-Semantik statt Zustandsabgleich erfüllbar). B hält einen Zustand, der nicht dem Gerät entspricht (Befehl verloren oder Handschalter), und unterdrückt deshalb das nächste "ein" ("war schon ein"). Schaltet die Steckdose in HA um, ändert sich nichts im Backend, weil `state_changed` für den Schaltzustand nicht in AD-18 vorgesehen ist.
- Echo-Schleife: `Ausschalten` schaltet Steckdose 2 aus, das spät eintreffende Leistungsereignis vor dem Ausschalten (60 W) kommt nach `AbwesendGeworden` an (Z4 A) und beendet die Abwesenheit; die IP bleibt aus: erneutes `AbwesendGeworden`, erneutes `Ausschalten`, in einer Schleife, solange HA Werte nachliefert.

**Fix (Ergänzung AD-9 und AD-18)**

> **AD-9/AD-18, Ergänzung:** Der Schaltzustand einer Steckdose ist **nicht** Zustand des Backends. Aufträge sind blind-idempotent; kein Slice hält einen "Soll"- oder "Ist"-Schaltzustand. Eingehende `state_changed`-Ereignisse mit Schaltzustand werden ignoriert. Das Echo der eigenen Schaltbefehle (Leistung nach `aus`) ist durch die Regel in Z4 (Leistung in `Abwesend` ist No-op) wirkungslos. "Bereits ausgeschaltet bleibt unverändert" (CAP-15) heisst: es entsteht kein zweiter Auftrag aus demselben Event (Inbox), nicht "zustandsabhängiger Befehl".

---

## Z11 — Anwesenheit wird von mehreren Einstiegen implizit angelegt (medium)

**Zitate**

- AD-12: "IDs werden im Code erzeugt, nie von der Datenbank: `Guid.CreateVersion7(timeProvider.GetUtcNow())`." (unverändert; kein fachlicher Schlüssel, L10 des früheren Reviews nicht übernommen)
- AD-19: drei Commands ändern das Aggregate; wer es anlegt, steht nirgends.

**Szenario**

- **Einheit A:** `IpPraesenzAufnehmen` findet bei Erststart keine `Anwesenheit` und legt eine an.
- **Einheit B:** `BewegungAufnehmen` (HA-Ereignis kurz nach dem Start, parallel) legt auch eine an.
- Beide mit neuer GUID v7.

**Wo es bricht**

Zwei Zeilen für den einen Bewohner; Commands finden mal die eine, mal die andere (`FirstOrDefault`). Anfangszustand ist offen: `Anwesend` (A) oder `Abwesend` (B). Nach einem Neustart im Zustand `Abwesend` startet das Backend nach dem Aufstehen als `Anwesend`: Der Reset des Zustands löst nichts aus, aber jede Ausgangsannahme ändert das erste `AbwesendGeworden`.

**Fix (Ergänzung AD-12 und AD-19)**

> **AD-12, Ergänzung:** Aggregates mit genau einer Instanz (`Anwesenheit`, `Raumklima`) haben einen festen, konfigurierten fachlichen Schlüssel und werden beim Start der Migration einmal angelegt (Seed, Anfangszustand `Anwesend`); Commands legen sie nie an und liefern `NotFoundError`, wenn sie fehlen.

---

## Z12 — Meteomatics (P12) liegt in keiner Zugriffsregel (medium)

**Zitate**

- AD-8: "Binds: … alle Aufrufe an Telegram, MQTT und Home Assistant". AD-9: nur MQTT und HA. AD-18: nur HA.
- Capability Map: "P12 Dunkelheit bestimmen | Zuhause | AD-11, AD-15"; SPEC CAP-12: "anhand der Sonnenzeiten von Meteomatics", Open Question: "Zugangsdaten, Kosten und Limits, … Verhalten bei Nichterreichbarkeit".
- AD-8 Prevents: "lange offene Transaktionen".

**Szenario**

- **Einheit A – Einrichten:** Ruft Meteomatics (HTTP, Internet) direkt im Command-Handler auf, innerhalb der Outbox-Konsumenten-Transaktion; kein Verstoss, denn Meteomatics steht in keiner Regel.
- **Einheit B:** Ein Zeit-Trigger holt täglich die Sonnenzeiten, speichert sie in einer eigenen Tabelle; Einrichten liest aus dieser.
- Offene Unterschiede: `Dunkel` auf dem Zeitpunkt des Events oder von jetzt; Verhalten bei Fehler (Exception → Retry-Schleife bis Internet da ist; oder "nicht dunkel").

**Wo es bricht**

A hält die Transaktion (und die Verbindung, die sich alle Modul-DbContexts teilen, AD-14) während des HTTP-Aufrufs offen; bei Timeout blockiert der Einrichten-Konsument die Outbox-Queue (Z7). B und A rechnen `Dunkel` für dieselben Eingaben verschieden.

**Fix (Ergänzung AD-8/AD-9 oder neue Rule)**

> **AD-8, Ergänzung:** Lesende Aufrufe an Umsysteme (Meteomatics) laufen **nie** innerhalb einer Transaktion eines Command-Handlers. Sonnenzeiten werden von einem Zeit-Trigger im Voraus geholt und in Zuhause gespeichert; `Dunkel` ist eine reine Funktion der gespeicherten Sonnenzeiten und des fachlichen Zeitpunkts. Fehlen Sonnenzeiten, gilt `Dunkel` nach einer festen Konfiguration (Uhrzeiten), nicht per Exception.

---

## Z13 — Zwei Home-Assistant-Adapter, weil der Vertrag erst "vor P10" kommt (medium)

**Zitate**

- AD-18: "Nur das Modul Zuhause spricht Home Assistant über die WebSocket-API … der Adapter liegt hinter einer Schnittstelle, Tests setzen einen Fake ein."
- Deferred: "Home-Assistant-Vertrag … eigenes Slice vor P10."; Capability Map: P9 hängt an AD-18, hat aber (CAP-9) die Leistung der Steckdose als Eingang.

**Szenario**

- **Einheit A – P9:** Baut `ILeistungQuelle` mit eigener WebSocket-Verbindung und eigenem Token-Handling, weil P9 vor dem "HA-Slice" kommt.
- **Einheit B – P10:** Baut `IHomeAssistantGateway` mit eigener Verbindung, eigenem Reconnect.

**Wo es bricht**

Zwei Verbindungen, zwei Reconnect-Strategien, zwei Fakes im Test; AD-9 ("nur Zuhause spricht Home Assistant") wird buchstabengetreu eingehalten, denn beide liegen in Zuhause. HA-Sperren oder Rate-Limits des Long-Lived-Tokens treffen beide.

**Fix (Ergänzung AD-18)**

> **AD-18, Ergänzung:** Zuhause hat **genau einen** Home-Assistant-Adapter (eine WebSocket-Verbindung, ein Gateway-Interface, ein Fake), den P9 als erster Slice anlegt und alle weiteren Slices benutzen. Das Slice "Home-Assistant-Vertrag" aus Deferred geht **vor P9**, nicht vor P10.

---

## Z14 — Status-Etiketten uneinheitlich (low)

**Zitate:** AD-1 bis AD-14 tragen `[ADOPTED]`; AD-15, AD-16, AD-17, AD-19 tragen kein Etikett; AD-18 trägt "`[ADR 0004, vorgeschlagen]`". Die SPEC (Constraints) nennt die Spine "bindend", ADR 0004 "noch nicht vom Bewohner bestätigt".

**Szenario:** Ein Agent liest "vorgeschlagen" als "noch nicht bindend" und baut P9 mit REST-Polling statt WebSocket; ein anderer liest "bindend" laut SPEC und baut WebSocket. AD-19 verweist auf AD-18 und ist damit teilbindend.

**Fix:** Einheitliches Statusfeld pro AD (`[ADOPTED]`, `[PROPOSED]`), und eine Regel: AD-19 und die Capability-Zeilen für P9 bis P14 hängen an AD-18 erst mit ADR 0004 `[ADOPTED]` (Bestätigung durch den Bewohner).

---

## Empfohlene Reihenfolge

1. Vor dem ersten Zuhause-Slice (P9): Z1, Z2, Z3, Z4, Z6, Z11, Z13. Sie bestimmen das Aggregate, die Einstiege und den HA-Adapter.
2. Vor P10/P11/P13: Z5, Z7, Z8, Z9, Z10. Sie bestimmen Zustellung, Einrichten und das Raumklima.
3. Vor P12: Z12. Vor Freigabe von AD-18: Z14.
