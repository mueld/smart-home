# Smart Home

Steuerung und Überwachung der Geräte im eigenen Haushalt. Zu den Geräten gehören ein Klimagerät, ein Bewegungsmelder am Eingang und schaltbare Steckdosen; genutzt wird das System von einer einzigen Person. Beim Nachhause-Kommen richtet es die Wohnung automatisch ein.

Der Code ist englisch (ADR 0005, vorgeschlagen). Jeder Begriff trägt seinen Code-Namen in der Zeile `_Code_`; sie ist die einzige Übersetzungstabelle.

## Language

### Geräte

**Klimagerät**:
Ein einzelnes, eigenständig ansteuerbares Gerät zum Kühlen/Heizen (aktuell eine Midea PortaSplit). Die Temperaturregelung übernimmt das Gerät selbst.
_Code_: `AirConditioner`
_Avoid_: Klimaanlage, AC, Klima, Unit

**Modus**:
Die Betriebsart eines Klimageräts: Kühlen, Heizen, Entfeuchten oder Lüften.
_Code_: `Mode` (`Cool`, `Heat`, `Dry`, `Fan`)
_Avoid_: Mode, Betriebsmodus

**Fähigkeiten**:
Die Menge der Einstellungen und Werte, die ein konkretes Klimagerät tatsächlich unterstützt (z. B. Eco, Swing, Lüfterstufen). Sie werden vom Gerät erfragt, nicht angenommen.
_Code_: `Capabilities`
_Avoid_: Features, Capabilities, Funktionen

**Bewegungsmelder**:
Ein Matter-Gerät am Eingang der Wohnung, das meldet, wenn es Bewegung erkennt. Es gibt genau einen.
_Code_: `MotionSensor`
_Avoid_: Sensor, PIR, Präsenzmelder

**Steckdose**:
Eine schaltbare Matter-Steckdose, die ein- und ausgeschaltet werden kann und ihre Leistung misst. Es gibt n davon; Steckdose 1 versorgt das **Licht**, Steckdose 2 den **Schreibtisch**.
_Code_: `SmartPlug` (Licht: `Light`, Schreibtisch: `Desk`)
_Avoid_: Plug, Schalter, Zwischenstecker

**Leistung**:
Die von einer Steckdose gemessene momentane Leistungsaufnahme in Watt.
_Code_: `Power`
_Avoid_: Stromverbrauch, Watt-Wert

### Überwachen

**Zustand**:
Die einstellbaren Werte eines Klimageräts zu einem Zeitpunkt, z. B. ein/aus, Modus, Solltemperatur, Lüfterstufe.
_Code_: `AirConditionerState`
_Avoid_: Status, Einstellungen, Config

**Messwert**:
Ein vom Klimagerät gemeldeter, nicht einstellbarer Wert, z. B. Raumtemperatur oder Aussentemperatur.
_Code_: `Reading` (Raumtemperatur: `IndoorTemperature`, Aussentemperatur: `OutdoorTemperature`)
_Avoid_: Sensorwert, Telemetrie

**Messung**:
Eine Momentaufnahme von Zustand und Messwerten eines Klimageräts zum Zeitpunkt der Abfrage. Das Klimagerät wird jede Minute abgefragt; antwortet es nicht, entsteht keine Messung.
_Code_: `Measurement`
_Avoid_: Snapshot, Abfrageergebnis, Datenpunkt

**Verlauf**:
Die zeitliche Abfolge aller Messungen eines Klimageräts, unverdichtet. Zeiten ohne Messung (z. B. offline) bleiben echte Lücken.
_Code_: `History`
_Avoid_: Historie, Log, Timeline

**Fehlercode**:
Ein vom Klimagerät gemeldeter Code ungleich 0, der eine Störung anzeigt.
_Code_: `ErrorCode`
_Avoid_: Error, Störungsmeldung

**Offline**:
Ein Klimagerät gilt als offline, wenn es 2 Minuten lang nicht geantwortet hat.
_Code_: `Offline`
_Avoid_: nicht erreichbar, down

**Benachrichtigung**:
Eine Telegram-Nachricht über ein Ereignis, das Aufmerksamkeit braucht: Klimagerät offline, wieder online oder Fehlercode.
_Code_: `Notification`
_Avoid_: Alarm, Alert, Notification

### Anwesenheit

**Abwesend**:
Der Bewohner gilt als abwesend, wenn die IP-Adresse 192.168.1.29 (sein Telefon) 2 Minuten lang nicht im Netzwerk geantwortet hat. Zieht der Schreibtisch mehr als 50 W, gilt er trotzdem als anwesend.
_Code_: `Away` (Event `BecameAway`)
_Avoid_: weg, nicht zu Hause, Away

**Anwesend**:
Der Bewohner ist nicht abwesend. Eine Abwesenheit endet erst mit dem Nachhause-Kommen.
_Code_: `Present`
_Avoid_: zu Hause, Home

**Nachhause-Kommen**:
Die erste Bewegung des Bewegungsmelders, während der Bewohner abwesend ist. Es beendet die Abwesenheit und richtet die Wohnung ein. Die IP spielt dafür keine Rolle: Die Rückkehr der IP allein ist kein Nachhause-Kommen und beendet die Abwesenheit nicht.
_Code_: `Homecoming` (Event `HomecomingDetected`)
_Avoid_: Heimkommen, Ankunft, Arrival

**Dunkel**:
Es ist dunkel zwischen Sonnenuntergang und Sonnenaufgang gemäss den Sonnenzeiten von Meteomatics.
_Code_: `Dark`
_Avoid_: Nacht, Abend

**Warm**:
Es ist warm, wenn die Raumtemperatur über 23.5 °C liegt. Ist die Raumtemperatur nicht zuverlässig, ist es warm, wenn die Aussentemperatur über 25 °C liegt.
_Code_: `Warm`
_Avoid_: heiss, Hitze

### Steuern

**Einrichten**:
Was beim Nachhause-Kommen geschieht: Der Schreibtisch wird eingeschaltet, das Licht nur wenn es dunkel ist, und das Klimagerät nur wenn es warm ist (Modus Kühlen, iECO an, Solltemperatur 22 °C).
_Code_: `SetUpHome`
_Avoid_: Begrüssung, Szene, Willkommen

**Ausschalten**:
Was beim Abwesend-Werden geschieht: Licht, Schreibtisch und Klimagerät werden ausgeschaltet.
_Code_: `ShutDownHome`
_Avoid_: Aufräumen, Abschalten

### Module und Aggregate

**Zuhause** (Modul): `Home`
**Klimagerät** (Modul): `AirConditioner`
**Anwesenheit** (Aggregate): `Presence`
**Raumwerte** (Aggregate): `RoomReadings`
**Steuerung** (Modul): `Control`
**Benachrichtigung** (Modul): `Notifications`
