# Gerätezugriff über Python-Hilfsdienst, angebunden per MQTT

Das Midea-LAN-Protokoll (V3, Token/Key-Handshake) wird nicht in C# nachgebaut. Stattdessen spricht ein kleiner Python-Hilfsdienst auf Basis von msmart-ng mit dem Klimagerät, und das .NET-Backend kommuniziert mit ihm ausschliesslich über MQTT. msmart-ng ist erprobt; ein eigener Protokoll-Port wäre riskant und lenkt vom eigentlichen Lernziel ab. Weil das Backend nur MQTT kennt, lässt sich der Hilfsdienst später austauschen.

Der Hilfsdienst bleibt bewusst dünn: Er fragt das Gerät minütlich ab, veröffentlicht Zustand, Messwerte und Fähigkeiten und führt Befehle aus. Fachlogik wie Offline-Erkennung, Zeitpläne, Übersteuerung und Benachrichtigungen liegt ausschliesslich im .NET-Backend.

## Considered Options

- **Eigene C#-Implementierung des V3-Protokolls**: verworfen, hohes Risiko beim Handshake und keine Referenz ausser dem Python-Code.
- **Kafka statt MQTT**: verworfen. Kafka braucht auf dem Raspberry Pi 1–2 GB RAM und Java, und Befehle mit Rückmeldung (Request/Reply) müsste man von Hand bauen. MQTT ist leichtgewichtig und im Smart Home der Standard.
