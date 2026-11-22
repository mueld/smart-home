# Story 1 – Home-Assistant-Gateway für Matter-Geräte

Status: needs-review (spec_checkpoint)
Quelle: SPEC CAP-10, Architecture Spine AD-18, AD-19; ADR 0004 (vorgeschlagen)

## Problem Statement

Bewegungsmelder und Steckdosen sprechen Matter und hängen an meinem Home Assistant. Das Backend hat heute keinen Weg dorthin. Ohne eine verlässliche Verbindung kann kein Prozess (Anwesenheit, Nachhause-Kommen, Einrichten, Ausschalten) auf Geräte reagieren oder sie schalten. Home Assistant liefert Ereignisse nur an die gerade offene Verbindung und ohne Wiederholung; fällt die Verbindung aus, sind Änderungen weg, und das Backend würde mit einem falschen Bild weiterarbeiten.

## Solution

Das Modul Home bekommt **genau einen** Home-Assistant-Adapter hinter einem Gateway-Interface (AD-18). Er hält die WebSocket-Verbindung, authentifiziert sich, hört per `state_changed` auf Änderungen, filtert auf die konfigurierten Entitäten (Bewegungsmelder und Steckdosen) und meldet jede Zustandsänderung als technisches Signal an Home. Bei jedem Verbindungsaufbau liest er zuerst den aktuellen Zustand aller konfigurierten Entitäten neu ein. Reißt die Verbindung ab, baut er sie mit Backoff wieder auf.

Diese Story liefert nur den Transport. Sie kennt keine Fachbegriffe wie Bewegung, Anwesenheit oder Leistung; die Commands `RecordMotion` und `RecordPower` entstehen in den Stories 2, 3 und 5, das Schalten in Story 3.

### Ablauf

```
Start → Verbindung öffnen → auth_required → auth (Token) → auth_ok
  → Zustand der konfigurierten Entitäten lesen (get_states)   → ZustandGelesen je Entität
  → Änderungen abonnieren (nur konfigurierte Entitäten)
  → Änderung kommt an                                           → StateChanged
  → Verbindung reißt ab → Backoff → wieder von vorn (Zustand neu lesen, Abo neu aufbauen)
```

## User Stories

1. Als Entwickler möchte ich, dass nur das Modul Home mit Home Assistant spricht, damit Matter- und Home-Assistant-Zugriff nicht über mehrere Module verstreut ist.
2. Als Entwickler möchte ich eine Gateway-Schnittstelle mit einem Fake, damit alle weiteren Home-Slices ohne echtes Home Assistant getestet werden können.
3. Als Entwickler möchte ich, dass es genau einen Adapter, eine Verbindung und ein Gateway-Interface gibt, die alle Home-Slices benutzen, damit sich Slices nicht auseinanderentwickeln.
4. Als Bewohner möchte ich, dass sich das Backend mit einem Token am Home Assistant anmeldet, das aus der Umgebung kommt, damit kein Geheimnis im Repository liegt.
5. Als Bewohner möchte ich, dass die Adresse von Home Assistant und die Entity-IDs Konfiguration sind, damit ich Geräte tauschen kann, ohne Code zu ändern.
6. Als Bewohner möchte ich, dass das Backend nur Änderungen des Bewegungsmelders (ein und aus) und der beiden Switches weiterverarbeitet und alle anderen `state_changed`-Events früh verwirft, damit der Event-Strom von Home Assistant das Backend nicht belastet.
7. Als Home-Slice möchte ich beim Start und nach jedem Verbindungsaufbau den aktuellen Zustand jeder konfigurierten Entität erhalten, bevor Änderungen einlaufen, damit ich keine Bewegung und keinen Leistungswert aus der Ausfallzeit verpasse.
8. Als Home-Slice möchte ich jede Zustandsänderung einer konfigurierten Entität mit Entity-ID, neuem Zustand, Attributen und fachlichem Zeitpunkt (Änderungszeit von Home Assistant) erhalten, damit ich daraus Commands bilden kann.
9. Als Home-Slice möchte ich Zustand und Zeitpunkt unterscheiden können zwischen „neu gelesen“ und „geändert“, damit ein neu gelesener Zustand keine Bewegung auslöst (Phantom-Bewegung nach Reconnect).
10. Als Home-Slice möchte ich Entitäten im Zustand `unavailable` oder `unknown` als solche erhalten, damit ich sie nicht mit „aus“ verwechsle.
11. Als Bewohner möchte ich, dass das Backend nach einem Verbindungsabbruch automatisch wieder verbindet, mit wachsendem Abstand und einer Obergrenze, damit ein Neustart von Home Assistant keinen Eingriff von mir braucht.
12. Als Bewohner möchte ich, dass das Backend auch startet und läuft, solange Home Assistant noch nicht erreichbar ist, damit die Reihenfolge der Container beim Hochfahren des Pi keine Rolle spielt.
13. Als Home-Slice möchte ich erfahren, ob das Gateway gerade verbunden ist, damit nachgelagerte Regeln bei veraltetem Wissen (Z6: eingefrorene Leistung, Fehlabwesenheit) vorsichtig sein können.
14. Als Bewohner möchte ich, dass eine falsche Anmeldung (`auth_invalid`) als Fehler sichtbar wird und nicht endlos mit Backoff wiederholt, damit ein falsches Token auffällt.
15. Als Betreiber möchte ich im Log sehen, wann das Gateway verbunden, getrennt oder abgemeldet war, damit ich Ausfälle nachvollziehen kann.

## Implementation Decisions

- **Modul und Grenze** (AD-9, AD-18): Der Adapter liegt im Modul Home; kein anderes Modul referenziert ihn. Er liegt hinter einer Schnittstelle (Arbeitsname `IHomeAssistantGateway`), die einen Fake für Tests erlaubt.
- **Schnittstelle (fachneutral)**:
  - Zustände lesen: liefert für alle konfigurierten Entitäten den aktuellen Zustand (Entity-ID, Zustand als Text, Attribute, Änderungszeit).
  - Änderungen beobachten: liefert einen Strom von Zustandsänderungen der konfigurierten Entitäten.
  - Verbindungszustand: verbunden oder nicht, plus Zeitpunkt der letzten Änderung dieses Zustands.
  - Schalten (Service-Aufruf) gehört zu Story 3 und fehlt hier bewusst.
- **Verbindungsvertrag** (AD-18): Home Assistant sendet höchstens einmal und ohne Replay; bei voller Ausgangs-Queue trennt es. Der Adapter baut deshalb nach jedem Verbindungsaufbau Auth und Abos neu auf und liest den Zustand neu, bevor er Änderungen weitergibt.
- **Reihenfolge nach Verbindungsaufbau**: Auth → Abo anlegen → `get_states` → Änderungen weitergeben. Das Abo steht vor dem Lesen, damit zwischen Lesen und Abo keine Änderung verloren geht; Änderungen, die älter sind als der gelesene Stand der Entität, werden verworfen. [ASSUMPTION: Vergleich über die Änderungszeit der Entität.]
- **Abo (entschieden 2026-11-22)**: Der Adapter hört per WebSocket auf `state_changed` (`subscribe_events` mit `event_type: "state_changed"`) und reicht nur Events der konfigurierten Entitäten weiter: den Bewegungsmelder (Wechsel auf `on` und auf `off`) und die beiden Switches (Steckdosen). Der Filter auf die Entity-IDs liegt im Adapter, denn Home Assistant filtert dieses Abo nicht nach Entität. Das Token-Konto braucht dafür Admin-Rechte.
- **Kein `to: on`-Filter**: Beide Richtungen (`on` und `off`) kommen durch, ebenso Wechsel nach `unavailable` und `unknown`. Die Abwesenheits- und die 50-W-Regel (Story 5) brauchen auch „aus“.
- **Leistung**: Bei Matter liefert Home Assistant die Leistung meist als eigene Sensor-Entität je Steckdose, nicht als Attribut des Switches. [ASSUMPTION: Das ist hier so; die Entity-ID der Leistung je Steckdose ist deshalb ebenfalls konfigurierte und gefilterte Entität, siehe Story 3 und 5.] Der Vertragstest klärt es.
- **Phantom-Bewegung vermeiden**: Das Signal trägt die Art `Read` oder `Changed`. Nur `Changed` kann später eine Bewegung ergeben; Story 2 setzt das um.
- **Reconnect**: Backoff mit festem Start (z. B. 1 s), Verdopplung und Obergrenze (z. B. 30 s); nach erfolgreichem Verbinden wieder auf den Start. [ASSUMPTION: konkrete Werte vom Bewohner nicht bestätigt.] Der Backoff ist in Tests über gesteuerte Zeit prüfbar.
- **Start ohne Home Assistant**: Das Backend startet unabhängig von der Erreichbarkeit; das Gateway verbindet im Hintergrund (Hosted Service).
- **Fehlerverhalten**: `auth_invalid` wird als Fehler protokolliert und beendet das Verbinden dauerhaft bis zum Neustart (kein Backoff-Sturm mit falschem Token). Alle anderen Abbrüche führen zum Reconnect.
- **Konfiguration** (Options-Pattern): Basis-Adresse von Home Assistant, Token (Secret aus der Umgebung), Liste der Entity-IDs mit Rolle (Bewegung, Steckdose n). Keine Entity-ID im Code (AD-18). Das Backend erreicht Home Assistant im Compose unter `http://host.docker.internal:8123` bzw. `ws://host.docker.internal:8123/api/websocket`. [ASSUMPTION: gilt mit `extra_hosts: host-gateway`.]
- **Nachrichten-ID**: Die `id` ist pro Verbindung aufsteigend und eindeutig; nach Reconnect beginnt sie neu.
- **Benennung im Code** gemäss ADR 0005: Der Code ist englisch, die Code-Namen stehen in CONTEXT.md (z. B. `StateChanged`).
- **Observability**: Strukturierte Logs für Verbunden, Getrennt, Abgemeldet, Wiederverbindung mit Versuchszähler.

## Testing Decisions

- **Seams**:
  1. *Gateway-Interface* für alle anderen Home-Slices: Dort wird der Fake benutzt; diese Story liefert den Fake mit.
  2. *Adapter gegen einen Fake-Home-Assistant*: Ein in-process WebSocket-Server (Kestrel-Testserver) spricht das Protokoll nach (auth_required, auth, get_states, Abo, Events) und lässt Verbindungsabbrüche und `auth_invalid` provozieren.
- Der Adapter wird über das beobachtbare Verhalten getestet (welche Signale kommen heraus, in welcher Reihenfolge, was sendet er auf die Leitung), nicht über interne Klassen.
- Abzudecken sind mindestens: erfolgreicher Verbindungsaufbau mit Auth; falsches Token; nur konfigurierte Entitäten werden gelesen und abonniert; Zustand wird vor Änderungen geliefert; Änderung nach dem Lesen kommt als `Changed` an; neu gelesener Zustand nach Reconnect ist `Read`; Verbindungsabbruch führt zu Reconnect mit Backoff (gesteuerte Zeit); Änderungen, die älter als der gelesene Stand sind, werden verworfen; `unavailable` und `unknown` kommen durch; Start bei nicht erreichbarem Home Assistant; Verbindungszustand wechselt korrekt.
- **Vertragstest gegen ein echtes Home Assistant**: nicht Teil der CI. Ein manuell auslösbarer Test (Kategorie, z. B. `Integration=HomeAssistant`) prüft einmal gegen die Instanz auf dem Pi, ob `state_changed` für den Bewegungsmelder und die beiden Switches im erwarteten Format ankommt (inkl. `on`/`off`) und ob die Leistung als eigene Entität kommt. Das klärt die ASSUMPTION oben.
- **Prior Art**: Der Kern der Testinfrastruktur (WebApplicationFactory, xUnit v3 mit Microsoft.Testing.Platform) stammt aus der Spec P2.

## Out of Scope

- Fachliche Commands (`RecordMotion`, `RecordPower`, `RecordIpPresence`): Stories 2, 3, 4, 5.
- Schalten von Steckdosen (Service-Aufruf, Outbox-Auftrag): Story 3.
- Kopplung der Matter-Geräte in Home Assistant und Betrieb des Matter-Servers (Betrieb, nicht Code).
- IP-Prüfung der Anwesenheit (macht das Backend selbst, nicht Home Assistant).
- Sonnenzeiten von Home Assistant (`sun`): Story 7 entscheidet darüber.
- Ressourcen- und Lasttests auf dem Pi.

## Further Notes

- **Offene Punkte aus der SPEC, die diese Story berühren**: Verhalten bei Ausfall von Home Assistant und Reconnect (Z6, Spine Deferred); Ressourcen auf dem Pi 5 und Netzwerk-Erreichbarkeit zwischen den Containern; Bestätigung von ADR 0004.
- **ADR 0004** ist noch `vorgeschlagen`. Diese Story setzt die WebSocket-Anbindung voraus. Wird der ADR abgelehnt, entfällt sie.
- **Token**: Ein langlebiges Zugriffstoken hat vollen Zugriff auf Home Assistant. Ablage als Dokploy-Secret bzw. Umgebungsvariable, Rotation ungeklärt (Spine-Review, Rubric B-Secrets).
- **Offen für die Umsetzung**: Welche Home-Assistant-Version läuft auf dem Pi? Die Abo-Art ist entschieden (`subscribe_events` auf `state_changed`); `subscribe_entities` wäre später eine mögliche Optimierung.
