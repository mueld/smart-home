---
id: SPEC-smart-home-backend
companions:
  - prozesse.md
  - ../../../CONTEXT.md
  - ../p2-zustand-und-messwerte-erfassen.md
  - ../../architecture/architecture-smart-home-2026-11-11/ARCHITECTURE-SPINE.md
  - ../../adr/0001-python-hilfsdienst-mit-mqtt.md
  - ../../adr/0002-verlauf-in-postgresql.md
  - ../../adr/0003-fachsprache-im-code.md
sources: []
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate. Source documents listed in frontmatter are for traceability — consult them only if you need narrative rationale or prose color this contract intentionally omits.

# Smart-Home-Backend

## Why

**Vision und Lernziel.** Der Bewohner will sein Klimagerät (Midea PortaSplit) selbst überwachen und seine Wohnung beim Nachhause-Kommen automatisch einrichten lassen: Licht, Schreibtisch und Klima sind bereit, ohne dass er etwas tun muss. Er will benachrichtigt werden, wenn etwas nicht stimmt. Zugleich ist das Projekt ein Lernprojekt für Agentic Engineering (Slices, TDD, PRs, ADRs); ein sauberer, nachvollziehbarer Weg zählt mehr als die schnellste Lösung.

## Capabilities

> CAP-6, CAP-7 und CAP-8 (Zeitsteuerung) sind seit 2026-11-21 zurückgezogen. Ihre IDs werden nicht wiederverwendet. CAP-15 steht hinter CAP-14, weil es zuletzt ergänzt wurde.

- **CAP-1**
  - **intent:** Das System kennt die Fähigkeiten des Klimageräts (z. B. Eco, Swing, Lüfterstufen), indem es sie vom Gerät erfragt statt sie anzunehmen.
  - **success:** Nach dem Start sind die vom Gerät gemeldeten Fähigkeiten abrufbar; eine nicht unterstützte Einstellung wird bei Befehlen als `ValidationFailed` abgelehnt.
- **CAP-2**
  - **intent:** Der Bewohner sieht den Aktuellen Zustand und den Verlauf des Klimageräts; jede Minute wird eine Messung aufgenommen und fachlich relevante Änderungen werden anderen Modulen gemeldet.
  - **success:** Alle Akzeptanzszenarien der Spec P2 sind durch Tests an der Backend-Grenze grün.
- **CAP-3**
  - **intent:** Der Bewohner erfährt per Telegram, wenn das Klimagerät offline geht (2 Minuten ohne Antwort) und wenn es wieder online ist.
  - **success:** Bleiben Messungen 2 Minuten aus, geht genau eine Benachrichtigung „offline“ hinaus; bei der nächsten Messung genau eine „wieder online“ (Duplikate aus at-least-once ausgenommen).
- **CAP-4**
  - **intent:** Der Bewohner erfährt per Telegram, wenn das Klimagerät einen neuen Fehlercode meldet.
  - **success:** Ein neuer Fehlercode ≠ 0 führt zu einer Benachrichtigung mit dem Code; ein weiterhin anstehender Code führt zu keiner weiteren.
- **CAP-5**
  - **intent:** Der Bewohner kann Solltemperatur, Modus, Ein/Aus und Selbstreinigung des Klimageräts manuell setzen.
  - **success:** Ein manueller Befehl erreicht das Klimagerät als Zielzustand, und die nächste Messung zeigt ihn.
- **CAP-9**
  - **intent:** Das System erkennt, dass der Bewohner abwesend ist, sobald die IP-Adresse 192.168.1.29 2 Minuten lang nicht mehr im Netzwerk antwortet. Zieht der Schreibtisch (Steckdose 2) mehr als 50 W, gilt der Bewohner auch ohne IP als anwesend.
  - **success:** Mit gesteuerter Zeit und Fakes für IP-Prüfung und Steckdose gilt der Bewohner nach 2 Minuten ohne Antwort der IP als abwesend, und zwar genau einmal pro Abwesenheit; bei kürzerem Ausbleiben bleibt er anwesend. Bei mehr als 50 W am Schreibtisch wird er trotz ausbleibender IP nicht abwesend.
- **CAP-10**
  - **intent:** Das System empfängt Bewegung vom Bewegungsmelder am Eingang, schaltet Steckdosen ein und aus und kennt deren gemessene Leistung; beide Geräte sind über Matter angebunden, und es gibt n Steckdosen.
  - **success:** Eine Bewegung am Melder erscheint im Backend als Ereignis mit fachlichem Zeitpunkt; ein Schaltbefehl für eine Steckdose beschreibt den Zielzustand (ein oder aus) und erreicht das Gerät; die zuletzt gemeldete Leistung einer Steckdose ist abrufbar.
- **CAP-11**
  - **intent:** Das System erkennt das Nachhause-Kommen: Nach einer Abwesenheit kehrt die IP ins Netzwerk zurück, und danach wird der Bewegungsmelder am Eingang aktiv.
  - **success:** Die erste Bewegung nach der Rückkehr der IP löst genau ein Nachhause-Kommen aus und beendet die Abwesenheit; die Rückkehr der IP allein löst nichts aus, und eine Bewegung bei Anwesenheit ebenfalls nicht.
- **CAP-12**
  - **intent:** Das System weiss, ob es dunkel ist, anhand der Sonnenzeiten von Meteomatics.
  - **success:** Mit gesteuerter Zeit und einem Fake für Meteomatics gilt es zwischen Sonnenuntergang und Sonnenaufgang als dunkel, sonst nicht.
- **CAP-13**
  - **intent:** Beim Nachhause-Kommen schaltet das System den Schreibtisch (Steckdose 2) ein und das Licht (Steckdose 1) nur, wenn es dunkel ist.
  - **success:** Ein Nachhause-Kommen schaltet Steckdose 2 ein; Steckdose 1 wird nur bei Dunkelheit eingeschaltet.
- **CAP-14**
  - **intent:** Beim Nachhause-Kommen schaltet das System die Klima ein, wenn es warm ist: Raumtemperatur > 23.5 °C. Ist die Raumtemperatur nicht zuverlässig, gilt stattdessen Aussentemperatur > 25 °C und das System holt danach den aktuellen Status des Klimageräts. Eingeschaltet wird immer mit Modus Kühlen, iECO an und Solltemperatur 22 °C.
  - **success:** Ein Nachhause-Kommen bei 23.6 °C Raumtemperatur erzeugt den Befehl Kühlen, iECO, 22 °C; bei 23.5 °C oder weniger entsteht keiner. Ein Test mit der Aussentemperatur deckt die Ersatzregel ab.
- **CAP-15**
  - **intent:** Wird der Bewohner abwesend, schaltet das System Licht (Steckdose 1), Schreibtisch (Steckdose 2) und Klima aus.
  - **success:** Mit dem Abwesend-Werden erhalten beide Steckdosen den Zielzustand aus und das Klimagerät den Befehl ausschalten; ein bereits ausgeschaltetes Gerät bleibt unverändert.

## Constraints

- Genau ein Nutzer, ein Klimagerät und ein Bewegungsmelder; die Steckdosen sind n. Das Klimagerät regelt die Temperatur selbst.
- Das Backend spricht mit dem Klimagerät nur über MQTT und einen Python-Hilfsdienst (ADR 0001). Bewegungsmelder und Steckdosen sprechen Matter; der Raspberry Pi 5 des Bewohners ist ein Home Assistant, und das Backend spricht ihn für Matter per WebSocket-API an (ADR 0004, vorgeschlagen). Fachlogik liegt ausschliesslich im Backend.
- Die Anwesenheit stützt sich auf die feste IP-Adresse 192.168.1.29.
- Die Dunkelheit stützt sich auf Meteomatics und braucht Internet.
- Persistenz in PostgreSQL (ADR 0002).
- Der Code ist englisch; die Code-Namen der Fachbegriffe stehen in CONTEXT.md (ADR 0005, vorgeschlagen).
- Die Architecture Spine (AD-1 bis AD-17) ist bindend, soweit sie dieses Update nicht berührt; AD-1, AD-9, AD-11 und AD-14 sind seit 2026-11-21 angepasst, neu sind AD-18 und AD-19.
- Betrieb auf einem Raspberry Pi mit Docker Compose; knappe Ressourcen schliessen schwere Zusatzdienste aus.
- Zugriff nur im LAN und über Tailscale; die App hat keine Anmeldung.
- Benachrichtigungen ausschliesslich über Telegram.
- Fachliche Zeiten gelten in Europe/Zurich.

## Non-goals

- Mehrere Nutzer, Rollen oder Anmeldung.
- Weitere Geräte oder Gerätetypen ausser dem Klimagerät, dem Bewegungsmelder und den Steckdosen.
- Eine eigene Implementierung des Midea-Protokolls.
- Eine UI – das Backend liefert nur die HTTP-Schnittstelle.
- Eigene Temperaturregelung (Regelkreis) im Backend.
- Verdichtung, Aufbewahrungsregeln oder Löschung von Verlaufsdaten.

## Success signal

- Eine Woche lang schalten sich beim Nachhause-Kommen der Schreibtisch, das Licht (nur bei Dunkelheit) und die Klima (nur bei Wärme) richtig ein, ohne dass eine Bewegung bei Anwesenheit etwas auslöst; ein Trennen des Klimageräts vom Strom löst innerhalb von 3 Minuten eine Telegram-Nachricht aus.

## Assumptions

- Die Fähigkeiten (CAP-1) werden beim Start des Backends bzw. beim ersten Kontakt mit dem Gerät erfragt.
- Manuelle Steuerung (CAP-5) läuft über die HTTP-Schnittstelle des Backends.
- Dunkel heisst zwischen Sonnenuntergang und Sonnenaufgang.
- Raumtemperatur und Aussentemperatur sind Messwerte der letzten Messung des Klimageräts, keine neue Wetterquelle.
- Steckdose 1 ist das Licht und Steckdose 2 der Schreibtisch; die Zuordnung ist fest, weitere Steckdosen kommen später dazu.

## Open Questions

- CAP-3: Ist „Klimagerät offline“ ein Event des Aggregates AirConditioner, und soll ein Ausfall von Hilfsdienst/MQTT unterschieden werden?
- CAP-4: Braucht es ein Event „Fehlercode behoben“?
- CAP-9/15: Wie wird die IP geprüft (z. B. Ping oder ARP)? Bei 2 Minuten sind Fehlabwesenheiten durch WLAN-Standby des Telefons möglich und schalten dann Licht und Klima aus; die 50-W-Regel schützt nur den Schreibtisch-Fall.
- CAP-11: Was gilt bei einer Bewegung, bevor die IP zurück ist (z. B. WLAN am Telefon aus)?
- CAP-9: Zieht der Schreibtisch mehr als 50 W, während der Bewohner schon abwesend ist, endet die Abwesenheit dann auch, oder gilt die Regel nur vor dem Abwesend-Werden?
- CAP-10: Braucht der Bewegungsmelder eine Entprellung oder Sperrzeit?
- CAP-10: ADR 0004 (Matter über Home Assistant per WebSocket-API) ist vorgeschlagen und braucht die Bestätigung des Bewohners. Home Assistant läuft als Container; das Backend läuft im selben Docker Compose daneben, ebenso ein eigener Matter-Server-Container. Offen bleiben die Ressourcen auf dem Pi 5 und die Netzwerk-Erreichbarkeit zwischen den Containern, ob die Leistungsmessung der Steckdose über Home Assistant ankommt und welches Modul die neuen Geräte und Prozesse besitzt.
- CAP-14: Wie alt darf der letzte Messwert für die Warm-Prüfung höchstens sein, und was gilt als „zuverlässig“?
- CAP-9/10: Home-Assistant-Ausfall und Reconnect: Verhalten der 50-W-Regel bei veraltetem Leistungswert (z. B. eingefrorener Wert über 50 W hält die Anwesenheit).
- CAP-12: Home Assistant kann den Sonnenstand (`sun`) liefern. Ersetzt das Meteomatics? Die IP-Präsenz prüft das Backend selbst (entschieden 2026-11-21).
- CAP-12: Meteomatics: Zugangsdaten, Kosten und Limits, Standort, Verhalten bei Nichterreichbarkeit (z. B. gecachte Sonnenzeiten oder feste Uhrzeiten), Dämmerung oder Sonnenuntergang als Grenze.
- CAP-14: Ist die Raumtemperatur bei ausgeschaltetem Klimagerät zuverlässig (der Bewohner prüft das)? Wie genau lautet danach die Ersatzregel, und was heisst „aktuellen Status holen“?
- Spine (Modul Home, AD-18, AD-19), CONTEXT.md und Spec P2 sind seit 2026-11-21 angepasst.
- Spine AD-17 und Spec P2: Wird `ReadingsReported` bei jeder Messung oder nur bei Änderung veröffentlicht, und wie stösst CAP-14 „aktuellen Status holen“ eine frische Messung an?
- Wie sieht der MQTT-Vertrag mit dem Hilfsdienst aus (Topics, Payload, QoS, `schemaVersion`)?
- Wird das Risiko akzeptiert, den Npgsql-Provider für EF Core 11 als RC einzusetzen?
