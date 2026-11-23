# Story 1 – Home-Assistant-Gateway für den Bewegungsmelder

Status: umgesetzt 2026-11-23 (Code in `Backend.Modules.Home`, Tests grün; Vertragstest gegen echtes Home Assistant steht aus)
Quelle: SPEC CAP-10, Architecture Spine AD-18, AD-19; ADR 0004 (vorgeschlagen)

> **Umfang geändert 2026-11-23 (Bewohner):** Das Gateway braucht nur das Ereignis des Bewegungsmelders, wenn er auf `on` wechselt. Jedes solche Ereignis wird als Domain Event `MotionDetected` über den Mediator veröffentlicht. Entfallen sind das Lesen des Zustands (`get_states`), die Signal-Arten `Read`/`Changed`, die Rollen und alle Steckdosen-Entitäten. Steckdosen und Leistung kommen mit den Stories 3 und 5.

## Problem Statement

Der Bewegungsmelder am Eingang spricht Matter und hängt an meinem Home Assistant. Das Backend hat heute keinen Weg dorthin. Ohne eine verlässliche Verbindung erfährt es nicht, dass jemand die Wohnung betritt. Home Assistant liefert Ereignisse nur an die gerade offene Verbindung und ohne Wiederholung; das Backend muss die Verbindung deshalb selbst am Leben halten.

## Solution

Das Modul Home bekommt **genau einen** Home-Assistant-Adapter (AD-18). Er hält die WebSocket-Verbindung, authentifiziert sich und abonniert einen `subscribe_trigger` auf den Bewegungsmelder mit `to: on`. Für jedes empfangene Ereignis veröffentlicht er `MotionDetected` über den Mediator (`IPublisher`), in einem eigenen DI-Scope (AD-15). Reißt die Verbindung ab, baut er sie mit Backoff neu auf.

Diese Story liefert nur den Transport und das Domain Event. Der Handler, der aus `MotionDetected` den Command `RecordMotion` macht, gehört zu Story 2.

### Ablauf

```
Start → Verbindung öffnen → auth_required → auth (Token) → auth_ok
  → subscribe_trigger {platform: state, entity_id: <Bewegungsmelder>, to: on}  → result success
  → Ereignis kommt an (event.variables.trigger.to_state.state == on)
      → publish MotionDetected(EntityId, DetectedAt = last_changed)   [eigener DI-Scope]
  → Verbindung reißt ab → Backoff → wieder von vorn
```

## User Stories

1. Als Entwickler möchte ich, dass nur das Modul Home mit Home Assistant spricht, damit Matter- und Home-Assistant-Zugriff nicht über mehrere Module verstreut ist.
2. Als Entwickler möchte ich genau einen Adapter und eine Verbindung für alle Home-Slices, damit sich Slices nicht auseinanderentwickeln.
3. Als Bewohner möchte ich, dass sich das Backend mit einem Token anmeldet, das aus der Umgebung kommt, damit kein Geheimnis im Repository liegt.
4. Als Bewohner möchte ich, dass Home Assistant-URL und die Entity-ID des Bewegungsmelders Konfiguration sind, damit ich das Gerät tauschen kann, ohne Code zu ändern.
5. Als Bewohner möchte ich, dass Home Assistant nur dann etwas an das Backend sendet, wenn der Bewegungsmelder auf `on` wechselt (ein Trigger für genau diesen Wechsel), damit das Backend nicht den ganzen Event-Strom verarbeitet.
6. Als Home-Slice möchte ich bei jeder Bewegung ein Domain Event `MotionDetected` über den Mediator erhalten, damit ich ohne Kenntnis von Home Assistant darauf reagieren kann.
7. Als Home-Slice möchte ich im Event die Entity-ID und den Zeitpunkt der Bewegung erhalten, wie Home Assistant ihn nennt (`last_changed`, UTC), nicht den Zeitpunkt der Verarbeitung (AD-8, AD-11).
8. Als Bewohner möchte ich, dass ein Fehler in einem Handler von `MotionDetected` die Verbindung nicht beendet, damit eine spätere Bewegung nicht verloren geht.
9. Als Bewohner möchte ich, dass das Backend nach einem Verbindungsabbruch automatisch wieder verbindet, mit wachsendem Abstand und einer Obergrenze, damit ein Neustart von Home Assistant keinen Eingriff von mir braucht.
10. Als Bewohner möchte ich, dass das Backend auch startet und läuft, solange Home Assistant noch nicht erreichbar ist, damit die Reihenfolge der Container beim Hochfahren des Pi keine Rolle spielt.
11. Als Home-Slice möchte ich erfahren, ob das Gateway gerade verbunden ist, damit nachgelagerte Regeln bei fehlendem Wissen vorsichtig sein können.
12. Als Bewohner möchte ich, dass eine falsche Anmeldung (`auth_invalid`) als Fehler sichtbar wird und nicht endlos wiederholt, damit ein falsches Token auffällt.
13. Als Bewohner möchte ich, dass das Gateway ohne URL, Token oder Bewegungsmelder-ID nur einen Fehler protokolliert und das Backend trotzdem startet.
14. Als Betreiber möchte ich im Log sehen, wann das Gateway verbunden, getrennt oder abgemeldet war.

## Implementation Decisions

- **Modul und Grenze** (AD-9, AD-18): Adapter, Protokoll und Domain Event liegen im Modul Home. Die Schnittstelle `IHomeAssistantGateway` meldet nur den `Status` (Zustand und Zeitpunkt der letzten Änderung); Bewegung kommt als Domain Event, nicht über die Schnittstelle. Schalten (Service-Aufrufe) und weitere Entitäten folgen in späteren Stories.
- **Abo (Protokoll vom Bewohner geprüft)**: `{"id": n, "type": "subscribe_trigger", "trigger": {"platform": "state", "entity_id": "<Bewegungsmelder>", "to": "on"}}`. Ein Ereignis hat die Form `event.variables.trigger` mit `entity_id`, `from_state` und `to_state` (`state`, `attributes`, `last_changed`, `last_updated`); `id` ist die Id des Abos. Der Adapter prüft Entität und `to_state.state == "on"` zusätzlich defensiv.
- **Domain Event**: `MotionDetected(EntityId, DetectedAt)` ist ein `DomainEvent` (Kernel, `INotification` des Mediators) und liegt in `Backend.Modules.Home.Domain`. Der Adapter veröffentlicht es mit `IPublisher.Publish` in einem eigenen DI-Scope pro Ereignis (AD-15). Es gibt keine Transaktion um das Publizieren; der Handler (Story 2) sendet einen Command, der seine eigene Transaktion hat (AD-6, AD-14).
- **Fehler im Handler**: Eine Exception aus `Publish` wird protokolliert und beendet die Verbindung nicht. Der Retry bei Konflikt (AD-16) liegt im Handler von Story 2.
- **Kein Lesen des Zustands**: Home Assistant liefert höchstens einmal und ohne Replay. Bewegung während einer unterbrochenen Verbindung geht verloren und wird nach dem Reconnect nicht nachgeholt; es wird nur neue Bewegung gemeldet. Für den Bewegungsmelder ist das gewollt (Nachhause-Kommen braucht eine neue Bewegung).
- **Reconnect**: Backoff mit Start 1 s, Verdopplung bis 30 s (konfigurierbar), nach einem erfolgreichen Verbinden wieder 1 s. `Connected` gilt, sobald Auth und Abo bestätigt sind. [ASSUMPTION: Werte vom Bewohner nicht bestätigt.]
- **Start ohne Home Assistant**: Das Backend startet unabhängig von der Erreichbarkeit; das Gateway verbindet im Hintergrund (Hosted Service).
- **Fehlerverhalten**: `auth_invalid` wird als Fehler protokolliert und beendet das Verbinden dauerhaft bis zum Neustart (Status `AuthenticationFailed`). Alle anderen Abbrüche führen zum Reconnect. Fehlt URL, Token oder Bewegungsmelder-ID, bleibt der Status `NotConfigured`.
- **Konfiguration** (Options-Pattern): `HomeAssistant__Url`, `HomeAssistant__Token` (Secret aus der Umgebung), `HomeAssistant__MotionSensorEntityId`; optional `HomeAssistant__DeskSwitchEntityId` und `HomeAssistant__DeskPowerEntityId` (seit 2026-11-23, gleiches Abo mit `to: null`), `InitialBackoff`, `MaxBackoff`, `ConnectTimeout`. Keine Entity-ID im Code (AD-18). Das Backend erreicht Home Assistant im Compose unter `http://host.docker.internal:8123`.
- **Nachrichten-ID**: Die `id` ist pro Verbindung eindeutig und beginnt nach einem Reconnect neu.
- **Benennung im Code** gemäss ADR 0005: Der Code ist englisch, die Code-Namen stehen in CONTEXT.md.
- **Observability**: Strukturierte Logs für Verbunden, Getrennt, Abgemeldet und Wiederverbindung mit Versuchszähler; das Token wird nie protokolliert.

## Testing Decisions

- **Seams**: (1) Der Adapter gegen einen Fake-Home-Assistant: ein in-process WebSocket-Server (Kestrel) spricht das Protokoll nach (auth_required, auth, subscribe_trigger, Ereignisse) und kann das Token ablehnen, Verbindungen abbrechen oder verweigern. (2) Der Mediator: Tests registrieren einen Handler für `MotionDetected` und beobachten die veröffentlichten Events.
- Getestet wird beobachtbares Verhalten: welche Domain Events ankommen, welchen Status das Gateway meldet und was der Adapter auf die Leitung sendet.
- Abgedeckt: Auth und Abo mit `to: on`; Domain Event mit Zeitpunkt von Home Assistant; ein echtes, vom Bewohner mitgeschnittenes Ereignis; fremde Entitäten und Wechsel nicht nach `on` werden nicht veröffentlicht; ein fehlschlagender Handler beendet die Verbindung nicht; falsches Token ohne Wiederholung; Reconnect mit erneutem Abo; Backoff mit gesteuerter Zeit; Start ohne erreichbares Home Assistant; fehlende Konfiguration.
- **Fake für andere Slices**: `FakeHomeAssistantGateway` setzt den Status. Bewegung braucht keinen Fake, ein Test veröffentlicht `MotionDetected` über den Mediator.
- **Vertragstest gegen ein echtes Home Assistant**: nicht Teil der CI, manuell auslösbar (Kategorie `Integration=HomeAssistant`). Er prüft einmal gegen die Instanz auf dem Pi, ob der `subscribe_trigger` mit `to: on` für den Bewegungsmelder ein Ereignis im erwarteten Format liefert und ob das Token-Konto Admin-Rechte braucht.

## Out of Scope

- Der Handler von `MotionDetected` und der Command `RecordMotion` (Story 2).
- Steckdosen: Schalten und Leistung (Stories 3 und 5); dort kommt bei Bedarf das Lesen des Zustands (`get_states`) dazu.
- Das Koppeln der Matter-Geräte in Home Assistant und der Betrieb des Matter-Servers.
- Eine Entprellung des Bewegungsmelders (entschieden 2026-11-23: keine, siehe Story 6).
- Ressourcen- und Lasttests auf dem Pi.

## Further Notes

- **Offen aus der SPEC**: Verhalten bei Ausfall von Home Assistant und Reconnect (Z6, Spine Deferred); Ressourcen auf dem Pi 5 und Netzwerk-Erreichbarkeit zwischen den Containern; Bestätigung von ADR 0004.
- **ADR 0004** ist `vorgeschlagen`. Wird er abgelehnt, entfällt diese Story.
- **Token**: Ein langlebiges Zugriffstoken hat vollen Zugriff auf Home Assistant. Ablage als Dokploy-Secret bzw. Umgebungsvariable, Rotation ungeklärt.
- **Compose-Dateien** tragen die neuen Variablen noch nicht; die Entity-ID des Melders (z. B. `binary_sensor.living_room_eingang_occupancy`) setzt der Bewohner.
