# Review Aktualität – Update 2026-11-21 (AD-18, AD-19, Modul Zuhause, Home Assistant als Container)

Prüfdatum: 2026-11-21. Geprüft: `ARCHITECTURE-SPINE.md` (AD-18, AD-19, Modul Zuhause, Stack-Tabelle) und `docs/adr/0004-matter-ueber-home-assistant.md`.
Methode: Web-Recherche gegen Primärquellen. Aussagen ohne Quelle sind ausdrücklich als **unbestätigt** markiert.

## Urteil

Die WebSocket-Anbindung, `sun`, Ping und die Matter-Cluster stimmen im Kern. Die Aussage „Home Assistant betreibt den Matter Server selbst“ gilt aber nur für Home Assistant OS; bei der in der Spine festgelegten Container-Installation stimmt sie nicht. Zusätzlich ist die Npgsql-Version in der Stack-Tabelle seit gestern überholt.

## Befunde

### B1 – critical/high: Matter Server bei Home Assistant im Container (ADR 0004, AD-18) – Aussage falsch für die gewählte Installationsart

- ADR 0004 (Absatz 2): „den Matter Server betreibt Home Assistant selbst“. AD-18: „Home Assistant betreibt den Matter Server“. Die Spine (Stack, Diagramm, ADR Consequences) legt aber **Home Assistant als Container** fest.
- Bestätigt: Der Matter Server läuft nur unter Home Assistant OS als App (früher Add-on). Bei Container-Installationen muss der Bewohner den Matter Server **selbst als separaten Docker-Container** betreiben und die Matter-Integration auf dessen URL zeigen lassen. Die HA-Doku nennt das „unsupported“ und „not recommended“.
  - https://www.home-assistant.io/integrations/matter/
- Bestätigt: Die Container-Installation „does not have access to apps“ (Add-ons gibt es nicht).
  - https://www.home-assistant.io/installation/linux/
- Bestätigt: Der Matter-Server-Container nutzt in allen Beispielen Host-Netzwerk (`--network=host`; mDNS braucht es). Standardport 5580 (`ws://…:5580/ws`), Image z. B. `ghcr.io/matter-js/matterjs-server:stable`. Er bindet standardmässig an alle Interfaces; die Doku rät zu Firewall oder authentifizierendem Proxy. Thread-Probleme sind bei selbst verwalteten Containern möglich.
  - https://github.com/matter-js/matterjs-server (Doku: `docs/docker.md`, https://raw.githubusercontent.com/matter-js/matterjs-server/main/docs/docker.md)
- Bestätigt: Das Repo `matter-js/matterjs-server` (Open Home Foundation, auf matter.js aufgebaut) ist API-kompatibler Ersatz des Python Matter Servers. Es ist die Quelle der HA-App. Der Hinweis auf den früheren `python-matter-server` ist damit veraltet.
- Bestätigt: Matter braucht lokales IPv6 und mDNS-Multicast, auf derselben VLAN wie HA; die Doku verlangt IPv6 in HA (Einstellungen > System > Netzwerk).
- Folgen:
  1. Die Spine-Topologie (Mermaid, Stack-Tabelle) kennt keinen Matter-Server-Container. Er fehlt in Docker Compose und in der Ressourcenfrage (ADR „Offen: Ressourcen auf dem Pi 5“).
  2. ADR 0004 widerspricht sich: Der Pi sei „bereits ein Home Assistant“ (Absatz 2), später „Home Assistant läuft als Container … im selben Docker Compose daneben“. Bei HAOS gäbe es kein Docker Compose für das Backend (nur über Supervisor/eigene Apps). Die Annahme muss mit dem Bewohner geklärt werden.
  3. Die Option „Eigener Matter Server neben Home Assistant: verworfen“ ist bei Container-HA gerade der Normalfall und kein zweiter Controller. Die Begründung „doppelter Betrieb“ trifft nicht zu; nötig ist genau ein Matter Server, nur eben vom Bewohner betrieben und mit HA verbunden.
  4. Das Commissioning (Smartphone mit Bluetooth, Thread Border Router) hängt an der Matter-Integration. Die Doku nennt Thread ausdrücklich für HAOS ab Version 10; für Container ist das **unbestätigt**. Betrifft hier nur dann, wenn Thread-Geräte gekauft werden (Bewegungsmelder und Steckdosen sind laut Spine Wi-Fi).
- Empfehlung: ADR 0004 und AD-18 so ändern, dass der Matter Server bei Container-Installation ein vom Bewohner betriebener, eigener Container im Compose ist (Host-Netzwerk, Port 5580 nur lokal). Oder die Annahme „Container“ gegen „HAOS“ bestätigen lassen. Zudem Host-Netzwerk von HA und Matter Server im Compose-Diagramm abbilden.

### B2 – medium: Npgsql-Provider in Stack-Tabelle überholt

- Spine: „Npgsql.EntityFrameworkCore.PostgreSQL 11.0.0-rc.3 (GA ausstehend – Risiko)“.
- Bestätigt (nuget.org Versionsliste): **11.0.0** stabil erschien am 2026-11-20; rc.3 war 2026-10-28.
  - https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL/
- Empfehlung: Eintrag auf 11.0.0 setzen und den Risikohinweis streichen. Der Hinweis zu AD-14 (Teilen der `NpgsqlConnection`) sollte kurz gegen 11.0.0 getestet werden; eine inhaltliche Änderung ist nicht belegt (**unbestätigt**).

### B3 – medium: PostgreSQL 18 ist nicht mehr die neueste Hauptversion

- Spine: „PostgreSQL 18“. Bestätigt: PostgreSQL 19 erschien am 2026-11-19; 18 ist weiter unterstützt (aktuell 18.7, EOL 2030-11-14).
  - https://www.postgresql.org/support/versioning/
- Die Aussage „18“ ist also nicht falsch, aber die Entscheidung gegen 19 sollte bewusst sein (frisch erschienenes .0-Release, Pi/ARM-Image und Npgsql-Kompatibilität mit 19 **unbestätigt**). Empfehlung: 18 beibehalten und die Begründung in einem Satz festhalten.

### B4 – medium: WebSocket-API – Eignung bestätigt, Details in der Spine/ADR ungenau oder offen

- Bestätigt: Ablauf `auth_required` → `auth` mit `access_token` → `auth_ok` bzw. `auth_invalid` (danach trennt der Server). Ein Langzeit-Token wird dort wie ein normales Zugriffstoken verwendet; es wird im Profil (Sicherheit) erzeugt und gilt 10 Jahre. Der Server speichert es nicht.
  - https://contributors.home-assistant.io/core/internals/websocket-api/
  - https://contributors.home-assistant.io/core/internals/auth-api/
- Bestätigt: `subscribe_events` mit `event_type: "state_changed"` liefert `event`-Nachrichten mit `entity_id`, `old_state`, `new_state`. Die meisten Eventtypen und „alle Events“ verlangen Admin-Rechte; das Token-Konto muss dafür passend sein (Hinweis für die Konfiguration, in AD-18 nicht erwähnt).
- Bestätigt: `call_service` mit `domain`, `service`, optional `service_data` und `target`; Antwort enthält `context`. Dazu `subscribe_trigger` (Admin) und `ping`/`pong` als Heartbeat (Intervall nicht festgelegt).
- Bestätigt: Jeder Befehl braucht eine pro Verbindung steigende numerische `id`; Fehlercode `id_reuse`. Ids sind pro Verbindung, nach Reconnect beginnt der Zähler neu.
- Neu gegenüber AD-18/ADR: Die Doku empfiehlt `subscribe_entities` als effizientere Variante zum Verfolgen von Zuständen. Ein C#-Client sollte zwischen `subscribe_entities` (kompaktes Diff-Format) und `subscribe_events` abwägen; ADR 0004 nennt nur `subscribe_events`. Das Format von `subscribe_entities` ist hier **unbestätigt** (nicht im Detail gelesen).
- **Reconnect:** Die Doku beschreibt kein Reconnect-Verfahren. Sie verweist auf die JS-Bibliothek `home-assistant-js-websocket`, die Reconnect und erneutes Abonnieren übernimmt. Für .NET gibt es keinen offiziellen Client (**unbestätigt**, nicht gesucht). Reconnect, Resubscribe und Zustandsabgleich nach Ausfall (z. B. `get_states`) muss das Backend selbst bauen. Die Spine stellt das zutreffend als offenen Punkt dar („Home-Assistant-Vertrag … Reconnect“ in Deferred). Gerade bei „Bewegung braucht Ereignisse“ ist wichtig: Ereignisse während eines Ausfalls gehen verloren, es gibt keinen Replay. Das ist eine Folgerung aus der Doku, die kein Replay erwähnt, und deshalb nur schwach belegt.
- Stabilität: Die Doku macht keine allgemeine Stabilitäts- oder Versionszusage für die WebSocket-API; nur manche Integrationsbefehle gelten als Vertrag, weil die Companion-Apps sie nutzen. Die Aussage „ist stabil“ ist also weder bestätigt noch widerlegt. Das Kernprotokoll (auth, subscribe, call_service) ist seit langem in Gebrauch; eine ausdrückliche Garantie fehlt.
- Eignung für ein .NET-Backend: grundsätzlich ja (reines JSON über WebSocket, `System.Net.WebSockets.ClientWebSocket` genügt). Das ist eine Einschätzung, keine Quellenaussage.
- Hinweis zu ADR 0004: Der Satz „Die REST-API … nur pollende Alternative“ ist korrekt. REST kann zwar Dienste aufrufen, liefert aber keine Ereignisse.

### B5 – low/medium: Matter-Cluster Occupancy Sensing und Electrical Power Measurement

- Bestätigt (Quellcode der HA-Core-Integration `matter`):
  - OccupancySensing → `binary_sensor` mit Geräteklasse `occupancy` (Bit 0 der Occupancy-Bitmap); Hue Motion Sensor als `motion`.
    - https://raw.githubusercontent.com/home-assistant/core/dev/homeassistant/components/matter/binary_sensor.py
  - ElectricalPowerMeasurement: `ActivePower` (Leistung), `Voltage`, `ActiveCurrent` werden zu Sensoren; ElectricalEnergyMeasurement: kumulierte Energie (Import/Export).
    - https://raw.githubusercontent.com/home-assistant/core/dev/homeassistant/components/matter/sensor.py
- Einschränkung: Das ist der `dev`-Zweig, nicht die vom Bewohner installierte Version. Die Doku-Seite der Integration listet keine unterstützten Cluster. Die Version, ab der das gilt, ist **unbestätigt**. Zusätzlich hängt es am Gerät (Matter 1.3 oder neuer, Firmware). Das deckt sich mit dem ADR-Satz „vor dem Gerätekauf prüfen“ (Quellen dazu nur Foren, daher schwach, z. B. https://matter-smarthome.de/en/practice/which-matter-smart-plugs-report-energy-data/).
- Hinweis: AD-18 spricht von „Leistung der Steckdosen“ – passend zu `ActivePower`. Die Entity-IDs und die Einheit (W) sind Konfiguration, wie in AD-18 festgelegt.

### B6 – low: `sun`- und Ping-Integration als Quellen (Deferred, ADR 0004)

- `sun`: bestätigt, standardmässig über `default_config` installiert (sonst `sun:` oder Dienste und Geräte). Liefert `sun.sun` (`above_horizon`/`below_horizon`) und Sensoren für nächste Morgendämmerung, Abenddämmerung, Aufgang, Untergang, Mittag, Mitternacht (UTC), dazu Elevation und Azimut. Berechnung aus dem konfigurierten Standort (IoT-Klasse „Calculated“). Die genauen Attributnamen und eine Internetfreiheit stehen nicht auf der Seite (**unbestätigt**).
  - https://www.home-assistant.io/integrations/sun/
  - Hinweis: Zeiten sind UTC (passt zu AD-11); die Zeitzone „Europe/Zurich“ ist Sache des Backends.
- Ping: bestätigt. Binary Sensor (standardmässig aktiv, „erreichbar“), Device Tracker (standardmässig **deaktiviert**, home/not_home, `consider_home` Standard 180 s), Standardintervall 30 s. Statische IP oder DHCP-Reservierung empfohlen.
  - https://www.home-assistant.io/integrations/ping/
  - Konflikt mit AD-11/Spine: Die Frist „Abwesend 2 Minuten“ ist kürzer als der Standard-`consider_home` von 180 s. Wird Ping aus Home Assistant genutzt, muss `consider_home` angepasst werden oder die Frist ganz im Backend gerechnet werden (nur Rohzustand lesen). Bei einer Ping-Quelle im Backend entfällt das Thema.
  - Container-Hinweis: Ping-Rechte (privilegiert bzw. `CAP_NET_RAW`) sind auf der HA-Seite **unbestätigt**; HA im Container läuft laut Doku-Beispiel ohnehin `privileged` mit `network_mode: host`. Für den Matter-Server-Container nennt die Doku `CAP_NET_RAW` bzw. `ping_group_range` nur für dessen eigenen Ping.
- Der Satz im ADR, `sun` und Ping „würden Meteomatics ersetzen“, ist nur für die Sonnenzeiten richtig (Ping ersetzt kein Meteomatics). Die Spine nennt Meteomatics als Quelle der Sonnenzeiten, in den bisher gelesenen Quellen taucht es sonst nicht auf (CONTEXT.md nicht geprüft). Empfehlung: Formulierung im ADR präzisieren.

### B7 – low: Stichproben Stack-Versionen seit 2026-11-11

| Eintrag in der Spine | Befund | Quelle |
| --- | --- | --- |
| .NET 11.0, GA 2026-11-10, STS bis 2028-11 | bestätigt (Seite nennt Ende des Supports 2028-11-14) | https://dotnet.microsoft.com/en-us/download/dotnet |
| EF Core 11.0 | bestätigt, 11.0.0 stabil seit 2026-11-10 | https://www.nuget.org/packages/Microsoft.EntityFrameworkCore |
| Npgsql EF Core 11.0.0-rc.3 | **überholt**, 11.0.0 seit 2026-11-20 (B2) | siehe B2 |
| FluentValidation 12.2 | bestätigt, 12.2.0 (2026-10-10) ist aktuell | https://www.nuget.org/packages/FluentValidation |
| PostgreSQL 18 | aktuell 18.7, aber 19 erschien 2026-11-19 (B3) | https://www.postgresql.org/support/versioning/ |
| Mosquitto 2.1 | bestätigt, 2.1.4 am 2026-11-17 (2.1.0: 2026-01-29) | https://mosquitto.org/blog/ |
| OpenTelemetry .NET 1.21 | bestätigt, 1.21.0 (2026-11-11) | https://www.nuget.org/packages/OpenTelemetry |
| Testcontainers für .NET 4.16 | **leicht überholt**, 4.17.0 seit 2026-11-16 | https://www.nuget.org/packages/Testcontainers |
| Aspire Dashboard 13.6 | **ungewiss**: Die GitHub-Seite zeigt 13.6.5 (2026-11-10) und danach „17.0.0“ (2026-11-18, „resource-first dashboard“). Ein Sprung von 13 auf 17 ist ungewöhnlich; ich konnte die Nummerierung nicht gegen eine zweite Quelle prüfen. Das Image `mcr.microsoft.com/aspire/dashboard` und seine Tags sind **unbestätigt**. | https://github.com/dotnet/aspire/releases |
| C# 15, Docker Compose v2 | nicht geprüft (unbestätigt) | – |

- Empfehlung: Testcontainers auf 4.17 anheben (Minor, geringes Risiko), Aspire-Dashboard-Tag vor der Festlegung am Image selbst prüfen, die Hauptversion 17 eventuell bewusst aussetzen und im Stack-Eintrag den Tag festschreiben. Auch die Deferred-Zeile „Aspire Dashboard 13.6 bietet optionale Persistenz“ ist damit unbestätigt (die Persistenz-Aussage selbst wurde nicht geprüft).

## Nicht gegen Quellen geprüft

- Verhalten von Matter-Geräten im Wi-Fi gegenüber Thread (Spine sagt „Matter, Wi-Fi“): nicht geprüft.
- Ressourcenbedarf auf dem Pi 5 und arm64-Images (Matter Server, Aspire Dashboard): die Doku nennt arm64 nicht ausdrücklich, daher **unbestätigt**.
- Offizielle oder verbreitete .NET-Clients für die HA-WebSocket-API: nicht gesucht.
- Format von `subscribe_entities`.
- Inhaltliche Prüfung der Fachregeln (Fristen, AD-8, AD-14): ausserhalb des Auftrags „Aktualität“.

## Zusammenfassung der Empfehlungen (nach Priorität)

1. ADR 0004 und AD-18: Matter-Server-Betrieb bei Container-HA korrigieren (eigener Container, vom Bewohner betrieben, nicht unterstützt von HA, Host-Netzwerk), Topologie ergänzen, Widerspruch „HAOS vs. Container“ klären.
2. Stack-Tabelle: Npgsql auf 11.0.0, Testcontainers auf 4.17, Aspire-Dashboard-Tag prüfen; PostgreSQL 18 bewusst beibehalten.
3. AD-18/ADR: `subscribe_entities` prüfen, Admin-Rechte des Tokens und fehlendes Replay bei Ausfall (Resync per `get_states`) in den Home-Assistant-Vertrag aufnehmen.
4. Deferred (Ping): `consider_home` 180 s gegen die 2-Minuten-Frist abwägen.
