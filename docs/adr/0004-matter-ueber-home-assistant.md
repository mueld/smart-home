# Matter-Geräte über Home Assistant, angebunden per WebSocket-API

Status: vorgeschlagen (2026-11-21), noch nicht vom Bewohner bestätigt. Ersetzt den ersten Entwurf „Matter Server per WebSocket“, der von einem Pi ohne Home Assistant ausging.

Auf dem Raspberry Pi 5 des Bewohners läuft Home Assistant als Container. Bewegungsmelder und Steckdosen (Matter, Wi-Fi) werden mit der Matter-Integration von Home Assistant eingebunden. Bei einer Container-Installation gibt es keine Add-ons; der Matter Server läuft deshalb als eigener Container im Compose (Image `ghcr.io/matter-js/matterjs-server`, Host-Netzwerk, WebSocket-Port 5580), und Home Assistant verbindet sich per WebSocket damit. Home Assistant stuft dieses Setup laut Review als nicht empfohlen ein (Quelle: home-assistant.io, Matter-Integration); das Risiko ist akzeptiert. Das .NET-Backend baut weder einen Matter-Controller nach noch betreibt es einen zweiten Matter Server. Es spricht Home Assistant über dessen WebSocket-API an: Es abonniert Zustandsänderungen (Bewegung, Leistung der Steckdose) und ruft Dienste auf (Steckdose ein/aus). Dafür genügt ein Langzeit-Zugriffstoken.

Wie bei ADR 0001 bleibt der Hilfsdienst dünn. Home Assistant liefert Geräte und Werte; Fachlogik wie Anwesenheit, Nachhause-Kommen, Einrichten und Ausschalten liegt ausschliesslich im Backend. Home-Assistant-Automationen werden dafür nicht verwendet, sonst läge Fachlogik ausserhalb des Backends und das Lernziel (Slices, TDD) entfiele. Der WebSocket-Adapter liegt hinter einer Schnittstelle, damit Tests einen Fake einsetzen.

Betroffen sind die Spine (AD-1: welches Modul besitzt die Geräte; AD-9: Gerätezugriff) und ADR 0001 („Das Backend kennt nur MQTT“ gilt nur für das Klimagerät).

## Considered Options

- **Eigener Matter-Controller in C#**: verworfen, hohes Risiko und kein Beitrag zum Lernziel.
- **Eigener Matter Server neben Home Assistant**: verworfen. Ein zweiter Controller auf demselben Pi bringt doppelten Betrieb und Commissioning für nichts, was Home Assistant schon kann.
- **Home-Assistant-Automationen statt Backend-Logik**: verworfen, Fachlogik läge ausserhalb des Backends.
- **Matter Server mit Bridge nach MQTT**: verworfen, zusätzlicher Container mit eigenem Code und Home Assistant hat die Geräte ohnehin.
- **Home Assistant per WebSocket-API**: gewählt. Die REST-API ist die einfachere, aber nur pollende Alternative; Bewegung braucht Ereignisse, also WebSocket.

## Consequences

- Home Assistant ist eine neue Abhängigkeit: Fällt er aus, fallen Bewegung und Schalten aus. Das Backend muss damit umgehen (Reconnect, Verhalten bei Ausfall).
- Das Langzeit-Token ist ein Secret in der Konfiguration (Umgebungsvariable, Options-Pattern).
- Geräte werden über Entity-IDs adressiert und sind Konfiguration, nicht Code (Steckdose 1 = Licht, Steckdose 2 = Schreibtisch, Bewegungsmelder).
- Die Leistungsmessung (Electrical Power Measurement) muss vom gewählten Gerät und von der Matter-Integration unterstützt werden; vor dem Gerätekauf prüfen.
- Befehle an Steckdosen beschreiben wie bei AD-9 immer einen Zielzustand (ein oder aus).
- Der Matter Server ist ein zusätzlicher Container; Commissioning einzelner Geräte kann im Container-Setup scheitern (IPv6, Host-Netzwerk). Das wird vor P9 geprüft.
- Home Assistant läuft als Container auf dem Pi 5. Das Backend, PostgreSQL und Mosquitto laufen im selben Docker Compose daneben, die Spine-Constraint „Docker Compose auf dem Pi“ bleibt gültig. Home Assistant ist im LAN über seinen Dienstnamen oder Host erreichbar; Matter braucht dort meist Host-Netzwerk, daher wird die Erreichbarkeit des Backends beim ersten Slice geprüft.
- Offen: Ressourcen auf dem Pi 5, wenn Home Assistant, Backend, PostgreSQL, Mosquitto und Aspire Dashboard zusammen laufen.
- Offen: Sonnenstand (`sun`) und IP-Präsenz (Ping) kann Home Assistant ebenfalls liefern. Das würde Meteomatics ersetzen. Entscheidung steht in der SPEC aus.
