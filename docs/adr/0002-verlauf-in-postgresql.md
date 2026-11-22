# Verlauf und letzte Messung in PostgreSQL

Messungen (Verlauf) und die letzte Messung pro Klimagerät werden in PostgreSQL gespeichert, angebunden aus dem .NET-Backend über EF Core. PostgreSQL läuft als eigener Container neben dem Backend auf dem Raspberry Pi. Bei einer Messung pro Minute (rund 525'000 Zeilen pro Jahr) genügt eine normale Tabelle mit Index auf dem Zeitpunkt der Abfrage. Der Zugriff ist hinter einer Schnittstelle gekapselt, sodass die Fachlogik PostgreSQL nicht kennt.

PostgreSQL ist zwar ein zusätzlicher Dienst auf dem Pi, lässt aber Raum für Wachstum: weitere Geräte, Auswertungen mit Grafana (native Datenquelle) und bei Bedarf TimescaleDB als Erweiterung, ohne die Datenbank zu wechseln. Tests am Seam laufen mit einem echten PostgreSQL via Testcontainers. Sie prüfen damit dasselbe Verhalten wie im Betrieb, auch über einen Neustart hinweg.

## Considered Options

- **SQLite (eingebettet)**: geringster Betriebsaufwand und einfachste Tests, verworfen zugunsten von Wachstum, Grafana-Anbindung und Mehrbenutzerzugriff.
- **TimescaleDB**: Hypertables und Kompression lohnen sich erst bei deutlich grösseren Datenmengen. Bleibt als spätere Erweiterung von PostgreSQL offen.
- **InfluxDB**: eigener Dienst mit eigener Abfragesprache und schwächerer .NET-Anbindung, bei dieser Datenmenge zu schwergewichtig.
- **Event Store (Marten, KurrentDB)**: würde Event Sourcing voraussetzen. P2 speichert aber Messungen und Read Models, nicht den Event-Stream als Quelle der Wahrheit.

## Consequences

- Auf dem Pi läuft ein zusätzlicher PostgreSQL-Container. Er braucht ein Volume für die Daten und ein Backup (z. B. `pg_dump`).
- Für Tests ist Docker nötig (Testcontainers), lokal und in der CI.
- Schemaänderungen laufen über EF-Core-Migrationen.
