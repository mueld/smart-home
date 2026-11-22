# Review (Rubric Walker): Architecture Spine, Update 2026-11-21

Prüfobjekt: `ARCHITECTURE-SPINE.md` (Stand `updated: 2026-11-21`, Zeilenangaben beziehen sich darauf).
Abgleich mit: `SPEC.md` (CAP-1–5, CAP-9–15), `prozesse.md`, `CONTEXT.md`, `p2-zustand-und-messwerte-erfassen.md`, ADR 0004.
Methode: Checkliste Punkt für Punkt, jeder Befund mit Stelle. Nichts wurde geändert ausser dieser Datei.

## Urteil

Die Spine ist nach dem Update im Kern tragfähig: Die neuen ADs AD-18 und AD-19 widersprechen AD-1 bis AD-17 nicht offen, und die Abhängigkeitsrichtung (Zuhause → Klimageraet, azyklisch) ist konsistent. Sie ist aber nicht `final`: Zwei Regeln (AD-16 gegenüber den Home-Assistant-Eingängen, AD-8 gegenüber einem Home-Assistant-Ausfall) verhindern die genannte Divergenz oder Fehlerklasse nicht, und das Betriebs- und Ausfallverhalten von Home Assistant ist so weit aufgeschoben, dass P9 (erster Zuhause-Prozess) davor umgesetzt würde. Kein Befund ist critical.

## Befunde

### B1 — AD-16 / AD-15 / AD-19: Verlust von Bewegung bei Konflikt auf dem Aggregate Anwesenheit [high]

- AD-19 (Z. 202): `Anwesenheit` ändert sich durch drei Commands (`IpPraesenzAufnehmen`, `BewegungAufnehmen`, `LeistungAufnehmen`), also durch drei unabhängige Einstiegspunkte (IP-Zeit-Trigger, Home-Assistant-Ereignis Bewegung, Home-Assistant-Ereignis Leistung). Es gibt genau einen Bewohner, also ein einziges Aggregate.
- AD-16 (Z. 184): „Ein Konflikt ist eine Exception (AD-10); Outbox-Konsumenten werden vom Worker erneut zugestellt.“ Für Einstiegspunkte, die nicht Outbox-Konsumenten sind (Home Assistant, Zeit-Trigger, MQTT), gibt es keine Wiederholung. Leistungsmeldungen von Steckdosen kommen häufig, ein Konflikt mit einem parallelen `BewegungAufnehmen` ist realistisch; die Bewegung ginge verloren, und damit das Nachhause-Kommen (CAP-11).
- Die Rule verhindert „verlorene Updates“ nur als Erkennung, nicht als Verlust der Eingabe. Offen: Wiederholung im Adapter, oder Leistung nicht im selben Aggregate wie die Anwesenheitslogik (AD-19 legt Leistung in `Anwesenheit` ab; CAP-10 verlangt die Leistung pro Steckdose, n Stück).

### B2 — AD-8 / AD-18: Ausfall von Home Assistant blockiert die gesamte Outbox von Zuhause [high]

- AD-8 (Z. 119): Zustellung „FIFO pro Modul und sequenziell“, und Aufträge an Home Assistant liegen in derselben Outbox wie die Integration Events (`NachhauseGekommen`, `AbwesendGeworden`, AD-19). Ist Home Assistant nicht erreichbar, blockiert der erste fehlgeschlagene Schaltauftrag alles dahinter, auch die Events, die Einrichten und Ausschalten auslösen.
- Nach der Wiederkehr würden veraltete Aufträge (z. B. Einrichten von vor Stunden) abgearbeitet. Eine Verfallszeit oder ein Überspringen gibt es nicht; „Retry-, Backoff- und Aufräumregeln“ stehen in Deferred (Z. 287), das Blockierverhalten (Head-of-Line) ist aber durch AD-8 bereits festgelegt und nicht aufgeschoben.
- AD-19 (Z. 202) stützt die 50-W-Regel auf `LeistungAufnehmen` aus Home Assistant. Bei Ausfall bleibt der letzte Wert stehen; die Spine sagt nicht, ob ein veralteter Wert noch zählt (SPEC CAP-9: „auch ohne IP als anwesend“). Das Ausfallverhalten steht nur als Stichwort in Deferred (Z. 290: „Verhalten bei Ausfall“) und ADR 0004 (Consequences: „Das Backend muss damit umgehen“).

### B3 — Deferred vs. Reihenfolge: Home-Assistant-Vertrag „vor P10“, aber P9 braucht ihn schon [medium]

- Deferred (Z. 290): „Home-Assistant-Vertrag (Authentifizierung, Reconnect, Event-Form, Verhalten bei Ausfall), Commissioning …, Ressourcen auf dem Pi 5: eigenes Slice vor P10.“
- Die Capability Map (Z. 278) ordnet P9 bereits AD-18 zu; P9 enthält die 50-W-Regel (SPEC CAP-9, `LeistungAufnehmen`), also Home-Assistant-Eingang. P9 würde somit vor dem Vertrag entstehen. Reconnect, Event-Form und Zeitpunkt-Regel sind genau die Punkte, an denen zwei Slices (P9, P10) auseinanderlaufen könnten.
- Zusätzlich fehlt eine Regel analog zu P2 („Zeitpunkt der Abfrage, ältere/gleiche verwerfen“) für Home-Assistant-Ereignisse: AD-8 verlangt den fachlichen Zeitpunkt nur für Integration Events; CAP-10 verlangt „Ereignis mit fachlichem Zeitpunkt“. Nach einem Reconnect sind doppelte oder verspätete `state_changed`-Ereignisse möglich; die Inbox (AD-8) greift nur für Outbox-Konsumenten, nicht für Home-Assistant-Eingänge.

### B4 — Betrieb/Umgebung: Deployment-Topologie, Ressourcen und Secrets nicht entschieden [medium]

- Stack (Z. 227) nennt Home Assistant „Container, beim Bewohner vorhanden“; das Diagramm (Z. 248–257) zeichnet ihn innerhalb von „Docker Compose“. ADR 0004 (Consequences) sagt zugleich, Matter brauche meist Host-Netzwerk und die Erreichbarkeit werde „beim ersten Slice geprüft“. Wem das Compose-File gehört (bestehendes Home-Assistant-Compose oder neues), wie das Backend Home Assistant adressiert (Dienstname/Host) und ob Host-Netzwerk gilt, ist weder entschieden noch als offene Frage der Spine geführt (nur indirekt Z. 290).
- Die Prüfverfahren der IP-Präsenz (Z. 289: „Ping/ARP oder Home Assistant“) hängen direkt an der Netzwerkart des Containers: ARP und ICMP aus einem Bridge-Container brauchen Rechte (z. B. `NET_RAW`) bzw. Host-Netzwerk. Das ist eine Deployment-Entscheidung, die mit P9 fällt, aber nirgends der Umgebung zugeordnet ist.
- Ressourcen Pi 5: aufgeschoben (Z. 290), obwohl PostgreSQL, Mosquitto, Hilfsdienst, Backend, Aspire Dashboard und Home Assistant zusammen laufen sollen. SPEC Constraints (Z. 72): „knappe Ressourcen schliessen schwere Zusatzdienste aus“; das Aspire Dashboard (Z. 228) ist ein solcher Dienst, und eine Speicher- oder CPU-Grenze pro Container fehlt.
- Secrets: AD-18 (Z. 196) und Konfiguration (Z. 213) legen Umgebungsvariablen fest. Der Home-Assistant-Token (Langzeit-Token, voller Zugriff) und der Telegram-Token liegen damit im Compose; Ablage und Rotation sind nicht entschieden. Deferred (Z. 291) nennt nur „Secrets/Absicherung von Mosquitto“.
- Migrationen beim Start oder separat (Z. 291) ist bei einem Pi mit Compose-Neustart ein echter Betriebspunkt, bleibt aber aufgeschoben (vertretbar, da zentral).

### B5 — P12 / CAP-12: Zugriff auf Sonnenzeiten ist durch keine AD geregelt [medium]

- Capability Map (Z. 280): P12 „Zuhause“, governed by AD-11, AD-15. Meteomatics erscheint weder im Stack noch im Deployment-Diagramm noch in AD-8/AD-18 (AD-8 bindet „Telegram, MQTT und Home Assistant“, Z. 117).
- Einrichten (AD-19) braucht „dunkel“ beim Nachhause-Kommen (CAP-13). Ob Zuhause das synchron in einem Handler per HTTP erfragt (lange offene Transaktion, genau was AD-8 verhindern will), oder per Zeit-Trigger Sonnenzeiten in das eigene Schema cached, ist nicht festgelegt. Deferred (Z. 289) schiebt nur die Quelle auf („Meteomatics oder `sun` aus Home Assistant“), nicht die Regel für Lese-Aufrufe an Umsysteme. Zwei Slices (P12, P13) könnten hier verschieden bauen. Zugangsdaten und Verhalten bei Nichterreichbarkeit sind ebenfalls offen (SPEC Open Questions Z. 108).

### B6 — CAP-14 „aktuellen Status holen“ und `MesswerteGemeldet` (AD-17) [medium]

- AD-17 (Z. 190): `MesswerteGemeldet` „mit jeder Messung“, „Zuhause hält daraus den letzten Wert“. SPEC Z. 111 und Spec P2 (Further Notes) führen „bei jeder Messung oder nur bei Änderung“ noch als offen; die Spine entscheidet es, die SPEC nicht. Entweder in der SPEC nachziehen oder in der Spine als vorläufig kennzeichnen.
- Die Ersatzregel von CAP-14 („holt danach den aktuellen Status des Klimageräts“) braucht einen Contract-Command Zuhause → Klimageraet, der eine frische Messung anstösst. AD-14 (Z. 155) nennt als Beispiel nur `SolltemperaturSetzen`; weder AD-9 noch AD-17 noch Deferred führen diesen Fall. Die Spine hat keine Regel, wie Zuhause mit einem veralteten letzten Wert umgeht (SPEC „zuverlässig“, Z. 109 offen); AD-8 verlangt zwar den fachlichen Zeitpunkt, aber kein Alter-Kriterium.
- Mengengerüst: `MesswerteGemeldet` 1/min erzeugt Outbox- und Inbox-Zeilen; die Aufräumregeln stehen nur als „Aufräumregeln der Outbox“ (Z. 287), die Inbox wird nicht genannt (low).

### B7 — AD-19 / AD-8 / AD-2: modulinterne „Integration Events“ und Rolle von Einrichten [medium]

- AD-19 (Z. 202): „Die Events sind modulintern; `Zuhause.Contracts` entsteht erst, wenn ein anderes Modul sie braucht.“ AD-2 (Z. 64) definiert Contracts als Ort der Integration Events, AD-8 (Z. 119) stellt sie „an die Handler anderer Module“ zu, und AD-17 (Z. 190) nennt interne Ereignisse Domain Events (`MessungAufgenommen` bleibt modulintern). AD-19 führt damit eine dritte Kategorie ein (Integration Event, nur im eigenen Modul konsumiert) ohne Ortsangabe (Typ liegt wo?) und ohne Aussage, dass AD-8 (Inbox, eigener Scope) auch für die Selbstzustellung gilt. Die Wahl ist plausibel (AD-6 verbietet den direkten Aufruf), die Rule ist aber an dieser Stelle nicht eindeutig.
- Einrichten ist ein Command, der Steckdosen-Aufträge schreibt und Commands an Klimageraet sendet, in einer Transaktion (AD-14). Ein `ValidationError` aus Klimageraet (z. B. nicht unterstützte Einstellung iECO, CAP-1) führt nach AD-14 zum Rollback des gesamten Einrichten, also auch der Steckdosen-Aufträge, und der Konsument wird nach AD-8/AD-16 erneut zugestellt, dauerhaft. Die Spine legt nicht fest, wie ein Fehler-Result in einem Outbox-Konsumenten behandelt wird (Wiederholen, verwerfen, Teilausführung). Zudem fehlt, ob Einrichten das Klimagerät mit einem Command (Zielzustand Kühlen + iECO + 22 °C) oder mehreren anspricht; bei mehreren entstehen Zwischenzustände (AD-9 Z. 125: ein Befehl, ein Outbox-Auftrag).

### B8 — Weitere Befunde [low]

- **AD-18 Durchsetzbarkeit:** „Home-Assistant-Automationen enthalten keine Fachlogik“ (Z. 196) liegt ausserhalb des Repos und ist durch keinen Test prüfbar. Architektur-Tests (Deferred Z. 291) decken nur AD-2 und AD-6 ab, nicht „nur Zuhause referenziert den Home-Assistant-Adapter“ (AD-9/AD-18) oder „MQTT nur in Klimageraet“.
- **Statusangaben:** Kopf `status: final` (Z. 8), aber AD-18 trägt „[ADR 0004, vorgeschlagen]“ (Z. 192), und AD-15, AD-16, AD-17, AD-19 haben gar kein Statusmarker, im Gegensatz zu AD-1 bis AD-14 („[ADOPTED]“).
- **Home-Assistant-Client:** Version „des Bewohners“ (Z. 227) und die Wahl zwischen eigenem WebSocket-Client und Bibliothek sind nicht festgelegt; Einlesen von `auth`, Abonnement und Dienstaufruf wäre sonst pro Slice verschieden. Die Aktualität der Technik (.NET 11 GA 2026-11-10, Npgsql 11.0.0-rc.3, Aspire Dashboard 13.6, Mosquitto 2.1) habe ich in diesem Durchgang nicht neu im Web verifiziert; für die neu hinzugekommene Home-Assistant-WebSocket-API gibt es keinen Versions- oder Quellenbeleg in der Spine.
- **Capability Map:** Sie führt Prozesse (P1–P14), keine CAP-IDs. Die Zuordnung CAP → P liegt nur in `prozesse.md`. Alle CAP-1–5 und CAP-9–15 sind über P1–P5 und P9–P14 abgedeckt; CAP-12 hat allerdings keine inhaltlich passende AD (siehe B5).
- **Drift SPEC/Prozesse:** SPEC Constraints (Z. 71) nennt „AD-1, AD-9, AD-11 und AD-14“ als angepasst, die Spine änderte zusätzlich AD-8, AD-15, AD-17. `prozesse.md` führt P9–P14 mit Modul „offen“ und „AD-1 (für P9–P14 offen)“, obwohl AD-1 Zuhause festlegt; SPEC Z. 106 führt „welches Modul besitzt die neuen Geräte“ weiter als offen.
- **Offene SPEC-Fragen ohne Spiegel in der Spine:** CAP-10 Entprellung der Bewegung, CAP-11 Bewegung vor Rückkehr der IP, CAP-9 50 W bei bereits Abwesendem. Sie sind fachlich, betreffen aber die Zustandsmaschine von `Anwesenheit` (AD-19), die die Spine nicht skizziert. Die Spine hat keinen Abschnitt „Offene Fragen“; Deferred führt sie nicht auf.

## Checkliste

| Prüfpunkt | Ergebnis |
| --- | --- |
| Echte Divergenzpunkte fixiert, keine ausgelassen | teilweise: Home-Assistant-Zeitpunkt/Duplikate (B3), Fehler-Result in Konsumenten und Einrichten-Granularität (B7), Zugriff auf Sonnenzeiten (B5) fehlen |
| Rule jeder AD durchsetzbar und verhindert die Divergenz | teilweise: AD-16 (B1), AD-8 (B2), AD-18 „keine Fachlogik in Automationen“ (B8) |
| Deferred ohne Divergenzgefahr | nein: Home-Assistant-Vertrag nach P9 (B3), Sonnenzeiten-Zugriff (B5), Retry/Blockieren (B2) |
| Technik verifiziert | nicht neu geprüft; keine Belege für Home-Assistant-Version/Client (B8) |
| Capability-Map vollständig | ja für CAP-1–5, CAP-9–15 über Prozesse; CAP-IDs fehlen, CAP-12 ohne passende AD (B5, B8) |
| Neue ADs schwächen/widersprechen AD-1–17 nicht | ja, ohne offenen Widerspruch; Unschärfe bei modulinternen Integration Events (B7); AD-6/AD-7/AD-14 gegenüber AD-19 konsistent (Domain-Event-Handler schreibt nur in die Outbox, Aufrufe an Klimageraet laufen im Outbox-Konsumenten); AD-2/AD-3 gegenüber `MesswerteGemeldet` konsistent (Zuhause hält den Wert im eigenen Schema) |
| Jede strukturelle Dimension entschieden/aufgeschoben/offen, besonders Betrieb | teilweise: Betrieb nur als Deferred-Stichworte, Topologie/Ressourcen/Secrets offen (B4) |
