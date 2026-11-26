# Prozesse

Katalog der Hauptprozesse. P1–P5 stammen aus dem EventStorming (Big Picture), nur P2 ist auf Process Level ausgearbeitet (Spec P2). P9–P14 sind neu seit 2026-11-21, noch ohne EventStorming; Events und Modulzuordnung sind vorläufig. Begriffe gemäss CONTEXT.md, Modulzuordnung gemäss Architecture Spine AD-1 (für P9–P14 offen).

| Prozess | CAP | Modul | Auslöser | Typische Events | Offene Hotspots |
| --- | --- | --- | --- | --- | --- |
| P1 Klimagerät einbinden | CAP-1 | AirConditioner | Start / erster Kontakt | Fähigkeiten erfragt | Zeitpunkt der Abfrage |
| P2 Zustand und Messwerte erfassen | CAP-2 | AirConditioner | jede Minute | MeasurementRecorded (intern); TargetTemperatureChanged, ModeChanged, Eingeschaltet, Ausgeschaltet, SelfCleanStarted, ErrorCodeReported (Integration Events) | – (geklärt) |
| P3 Erreichbarkeit überwachen | CAP-3 | AirConditioner (Erkennung), Benachrichtigung | 2 Minuten ohne Messung | Klimagerät offline, wieder online, Benachrichtigung gesendet | Offline als Event des Aggregates? Ausfall Hilfsdienst/MQTT unterscheiden? |
| P4 Störung melden | CAP-4 | Benachrichtigung | ErrorCodeReported | Benachrichtigung gesendet | „Fehlercode behoben“ nötig? |
| P5 Manuell steuern | CAP-5 | Steuerung → AirConditioner | Nutzer (HTTP) | Befehl gesendet | – |
| P9 Anwesenheit überwachen | CAP-9 | offen | IP 192.168.1.29 antwortet 2 Minuten nicht; Schreibtisch > 50 W | Bewohner abwesend | Prüfverfahren; 50-W-Regel bei bereits Abwesenden |
| P10 Matter-Geräte ansteuern | CAP-10 | offen | Bewegung vom Melder; Schaltbefehl | Bewegung erkannt, Steckdose geschaltet | Matter-Anbindung (eigenes ADR) |
| P11 Nachhause-Kommen erkennen | CAP-11 | offen | Bewegung erkannt bei Abwesenheit | Nachhause-Kommen erkannt | Besuch löst ebenfalls aus (akzeptiert) |
| P12 Dunkelheit bestimmen | CAP-12 | offen | Anfrage aus P13 | – | Meteomatics: Zugang, Standort, Ausfall |
| P13 Wohnung einrichten | CAP-13, CAP-14 | offen | Nachhause-Kommen erkannt | Schreibtisch eingeschaltet, Licht eingeschaltet, Klima eingeschaltet | Raumtemperatur zuverlässig? Ersatzregel |
| P14 Wohnung ausschalten | CAP-15 | offen | Bewohner abwesend | Licht ausgeschaltet, Schreibtisch ausgeschaltet, Klima ausgeschaltet | Fehlabwesenheit durch Telefon-Standby |

## Zurückgezogen (2026-11-21)

P6 Zeitplan verwalten (CAP-6), P7 Zeitplan ausführen (CAP-7) und P8 Übersteuerung und Wiederaufnahme (CAP-8) entfallen mit der Zeitsteuerung. Die Hotspots dazu sind hinfällig. Ein Befehl erzeugt weiterhin nur „…befehl gesendet“; ob er gewirkt hat, zeigt erst die nächste Messung (P2).
