# Story 6 – Nachhause-Kommen erkennen

Status: spezifiziert 2026-11-23 (noch nicht umgesetzt)
Quelle: SPEC CAP-11, CAP-9; Architecture Spine AD-6, AD-8, AD-11, AD-16, AD-19; CONTEXT.md (Abwesend, Anwesend, Nachhause-Kommen)
Voraussetzung: Story 2 (Command `RecordMotion`) und Story 4 (Aggregate `Presence`, `BecameAway`)

> **Entscheid 2026-11-23 (Bewohner):** Das Nachhause-Kommen ist die erste Bewegung bei Abwesenheit. Die IP spielt dafür keine Rolle; ihre Rückkehr beendet die Abwesenheit nicht. Der Zustand `Returned` entfällt. Keine Entprellung und keine Zeitregel gegen verspätete Bewegung.

## Problem Statement

Das System soll beim Heimkommen die Wohnung einrichten. Als Auslöser dient der Bewegungsmelder am Eingang. Das Telefon ist beim Betreten meist noch nicht im WLAN, deshalb darf die IP keine Voraussetzung sein.

## Solution

`Presence` kennt nur `Present` und `Away`. `RecordMotion` ist bei `Away` der einzige Weg zurück nach `Present` und erzeugt dabei genau ein Domain Event `HomecomingDetected`. Dessen Handler schreibt ein Integration Event in die Outbox von Home (AD-19); das Einrichten selbst folgt in den Stories 8 und 9. Bewegung bei `Present` und jede IP-Prüfung bei `Away` sind No-ops.

### Zustandsmaschine

| Zustand | Command | Ergebnis |
| --- | --- | --- |
| `Present` | `RecordMotion` | No-op |
| `Away` | `RecordMotion` | `Present`, `HomecomingDetected` |
| `Away` | `RecordIpPresence` (erreichbar oder nicht) | No-op |
| `Present` | `RecordIpPresence` | Frist wie in Story 4 (`BecameAway`) |

## User Stories

1. Als Bewohner möchte ich, dass die erste Bewegung am Eingang bei Abwesenheit das Nachhause-Kommen auslöst, damit die Wohnung bereit ist, ohne dass ich etwas tue.
2. Als Bewohner möchte ich, dass das Nachhause-Kommen auch auslöst, wenn mein Telefon noch nicht im WLAN ist, damit die übliche Reihenfolge Bewegung, dann WLAN funktioniert.
3. Als Bewohner möchte ich, dass nur die Rückkehr der IP nichts auslöst und die Abwesenheit nicht beendet, damit ein Telefon im WLAN ohne Bewegung die Wohnung nicht einschaltet.
4. Als Bewohner möchte ich, dass jede weitere Bewegung nach dem Nachhause-Kommen nichts mehr auslöst, damit die Wohnung nicht mehrfach eingerichtet wird.
5. Als Bewohner möchte ich, dass ein mehrfach ausgelöster Melder in Sekunden trotzdem nur ein Nachhause-Kommen erzeugt.
6. Als Bewohner möchte ich, dass Bewegung bei Anwesenheit (z. B. beim Verlassen der Wohnung) nichts auslöst.
7. Als Entwickler möchte ich, dass `HomecomingDetected` genau ein Integration Event in der Outbox ergibt, damit Einrichten (Stories 8, 9) als Konsument folgen kann (AD-8, AD-19).
8. Als Entwickler möchte ich, dass der fachliche Zeitpunkt der Bewegung im Event steht, nicht der der Verarbeitung (AD-8, AD-11).

## Akzeptanzszenarien

Tests an der Backend-Grenze: Zeit gesteuert (`TimeProvider`), Fake für die IP-Prüfung, `MotionDetected` über den Mediator veröffentlicht, Beobachtung über die Outbox von Home und den Zustand von `Presence`.

| # | Gegeben | Wenn | Dann |
| --- | --- | --- | --- |
| S1 | Bewohner ist abwesend, IP nicht erreichbar | Bewegung | Genau ein `HomecomingDetected`; Zustand `Present`; ein Integration Event in der Outbox mit dem Zeitpunkt der Bewegung |
| S2 | Bewohner ist abwesend, IP wieder erreichbar | Bewegung | Genau ein `HomecomingDetected` (IP unabhängig) |
| S3 | Bewohner ist abwesend | IP-Prüfung erreichbar, keine Bewegung | Kein Event, Zustand bleibt `Away` |
| S4 | Bewohner ist abwesend, IP war erreichbar, ist danach wieder weg | Bewegung | Genau ein `HomecomingDetected` (Abwesenheit endete nie durch die IP) |
| S5 | Bewohner ist anwesend | Bewegung | Kein Event, Zustand bleibt `Present` |
| S6 | Nachhause-Kommen ist erfolgt | zweite Bewegung kurz danach | Kein weiteres Event, genau ein Eintrag in der Outbox |
| S7 | Bewohner ist abwesend | zwei Bewegungen im Abstand weniger Sekunden | Genau ein `HomecomingDetected` |
| S8 | Bewohner ist anwesend, IP fällt weg | Bewegung vor Ablauf der 2 Minuten, danach Ablauf | Bewegung ist No-op; danach `BecameAway`; kein Nachhause-Kommen |
| S9 | Bewohner ist seit Stunden abwesend (IP lange aus), Nachhause-Kommen erfolgt zum Zeitpunkt T | IP-Prüfung kurz nach T, IP weiterhin aus | Kein sofortiges `BecameAway`: Die 2-Minuten-Frist beginnt mit dem Nachhause-Kommen bei T neu, nicht mit der letzten erreichbaren IP |
| S9b | wie S9 | IP bleibt bis T + 2 Minuten aus | `BecameAway` (Story 4): eine neue Abwesenheit beginnt, die nächste Bewegung löst wieder genau ein Nachhause-Kommen aus |
| S10 | Das Aggregate wird gleichzeitig durch IP-Prüfung und Bewegung geändert (Konflikt, `xmin`) | Command wird wiederholt | Bewegung geht nicht verloren (AD-16, höchstens 3 Versuche) |
| S11 | Das Integration Event wird zweimal zugestellt (at-least-once) | Zustellung | Der Konsument setzt es nur einmal um (Inbox, AD-8) |

## Implementation Decisions

- Die Regel liegt im Aggregate `Presence` (Operation für `RecordMotion`); Handler und Endpoint enthalten keine Fachlogik (AD-4, AD-15).
- Domain-Fehler: Das Aggregate liefert für Bewegung bei `Present` und IP-Prüfung bei `Away` keinen Fehler, sondern ein erfolgreiches No-op (Idempotenz). Der Use Case übersetzt das in ein Outcome ohne Fehlerfall (AD-10).
- Mit dem Nachhause-Kommen beginnt die 2-Minuten-Frist von Story 4 neu beim Zeitpunkt der Bewegung. Sonst würde die erste IP-Prüfung nach dem Heimkommen die lange zurückliegende letzte IP sehen und sofort `BecameAway` auslösen, und die Wohnung schaltete gleich wieder aus (S9).
- `RecordMotion` trägt den fachlichen Zeitpunkt der Bewegung (aus `MotionDetected.DetectedAt`, Story 2). Das Integration Event übernimmt ihn (AD-8, AD-11).
- Keine Entprellung, keine Sperrzeit, keine Zeitregel gegen verspätete Bewegung (Entscheid 2026-11-23). Bewegungen eines Ausfalls von Home Assistant gehen verloren (AD-18).
- `RecordPower` und die 50-W-Regel (Story 5) bleiben unberührt; die Frage nach der 50-W-Regel bei bereits Abwesenden ist weiter offen (SPEC CAP-9).
- Das Einrichten selbst (Schreibtisch, Licht, Klima) ist nicht Teil dieser Story (Stories 8, 9).

## Testing Decisions

- Seam: `WebApplicationFactory` mit PostgreSQL (Testcontainers); IP-Prüfung und Gateway als Fakes; Zeit über einen steuerbaren `TimeProvider`.
- Jedes Szenario S1–S11 (mit S9b) ist ein Test. S10 und S11 prüfen die Nebenläufigkeit (AD-16) und die Inbox (AD-8) einmal für Home.
- Vorgehen mit TDD in Scheiben: zuerst S1 und S5, dann S3, S2/S4, S6/S7, S8/S9, zuletzt S10/S11.

## Out of Scope

- Einrichten der Wohnung (Stories 8 und 9), Dunkelheit (Story 7).
- Telegram-Nachricht beim Nachhause-Kommen (nicht gefordert).
- Erkennen, wer die Bewegung auslöst (Besuch löst ebenfalls aus; akzeptiert).

## Further Notes

- Ein Ausfall von Home Assistant oder ein ausgefallener Melder lässt das System bis zur nächsten Bewegung in `Away`.
- Offen aus der SPEC (nicht Teil dieser Story): 50-W-Regel bei bereits Abwesenden, Fehlabwesenheit durch Telefon-Standby, Prüfverfahren der IP (Story 4).
