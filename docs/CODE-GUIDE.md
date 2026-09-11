# Den Code von finestats-lite verstehen

`finestats-lite` sammelt Spielereignisse eines CS2-Servers, berechnet daraus
Statistiken und Punkte und speichert sie lokal in SQLite. Spieler können diese
Daten über Chatbefehle abrufen. Es läuft kein zusätzlicher Webserver.

## Wo anfangen?

| Datei | Aufgabe |
|---|---|
| `FinestatsPlugin.cs` | Einstieg: Konfiguration lesen, Dienste verbinden, Ereignisse abonnieren; beim Entladen wieder abmelden. |
| `Config/StatsConfig.cs` | Einstellungen einlesen und ungültige Kombinationen ablehnen. |
| `Config/RankingOptions.cs` | Regeln für Punkte, Filter und Teilnahme an der Rangliste. |
| `Services/EventDispatcher.cs` | Die Warteschlange im Hintergrund in begrenzten Paketen abarbeiten. |
| `Services/SqliteStatsClient.cs` | Speicherung anstoßen, Datenbanksperren mit begrenzten Wiederholungen behandeln, danach Punktehinweise liefern. |
| `Storage/LiteStore.cs` | Datenbank anlegen, Ereignisse verbuchen, Identitäten zuordnen, Punkte berechnen und Backups schreiben. |
| `Storage/LiteQueries.cs` | Zweiter Teil derselben `LiteStore`-Klasse: Daten für Profile, Ranglisten und Detailansichten lesen. |
| `Services/StatsChatCommands.cs` | Chatbefehle registrieren, Anfragen begrenzen und Antworten an die passende Spielersitzung liefern. |
| `Services/StatsCommandClient.cs` | Befehle in lokale Abfragen übersetzen und deren Ergebnisse als Chattext formatieren. |
| `Services/ConnectChat.cs` / `ConnectionGate.cs` | Verbindungsnachrichten nach Bereitschaft und Authentifizierung genau einmal anstoßen. |
| `tests/Program.cs` | Ausführbare Prüfungen mit echten, temporären SQLite-Datenbanken. |

Die Pfade in dieser Tabelle sind relativ zum Ordner `finestats-lite`.

## Beispiel: Ein Spieler erzielt einen Kill

1. Der `CombatCollector` erfasst das Spielereignis. Der `CollectionContext`
   ergänzt unter anderem Server, Map, Collector und Zeitpunkt und legt das
   Ereignis in die `EventQueue`.
2. Der `EventDispatcher` entnimmt Ereignisse im Hintergrund. Er schreibt ein
   Paket, sobald es voll ist oder seine Wartefrist abläuft.
3. Der `SqliteStatsClient` übergibt das Paket an `LiteStore.AppendAsync`.
   Eine vorübergehend gesperrte Datenbank darf einen weiteren Versuch auslösen.
4. `AppendAsync` öffnet eine Transaktion. Bereits bekannte Ereignis-IDs liefern
   ihre gespeicherten Belege zurück; neue Ereignisse gelangen zu `Apply`.
5. `Apply` ordnet die beteiligten Spieler zu, berücksichtigt Warmup und Bots,
   erhöht passende Zähler und berechnet gegebenenfalls Punkte. Selbstmord,
   Teamkill und regulärer Kill werden getrennt behandelt.
6. Statistik und Ereignisbeleg werden gemeinsam bestätigt (`Commit`). Erst
   danach werden optionale Chatbenachrichtigungen verarbeitet. Ein Fehler im
   Chat darf einen erfolgreichen Schreibvorgang nicht in einen Fehlversuch verwandeln.

## Warum es Warteschlange und Hintergrundarbeit gibt

Callbacks aus dem Spiel müssen schnell zurückkehren. Datenbankzugriffe laufen
deshalb im Hintergrund. Die Warteschlange ist begrenzt: Bei Überlast können
Ereignisse verloren gehen, statt unbegrenzt Speicher zu verbrauchen.

Beim Entladen beendet `RequestStop` die Annahme neuer Ereignisse und setzt eine
Frist für die Restverarbeitung. Es wartet nicht synchron auf dem Spielthread.
Nach Ablauf der Frist zählt der Dispatcher nicht gespeicherte Ereignisse als
fehlgeschlagen.

Chatbefehle übernehmen nur benötigte Identitätsdaten in die Hintergrundarbeit.
Die eigentliche Antwort wird über `NextWorldUpdate` zurück ins Spiel gebracht.
Vor dem Senden wird die Sitzung erneut geprüft: Eine Antwort darf nach einem
Reconnect nicht beim neuen Benutzer desselben Spieler-Slots landen.

## Was die Datenbank enthält

| Tabelle | Bedeutung |
|---|---|
| `players` | Spieleridentität, Name und aktueller Punktestand. |
| `sessions` | Beobachtete Sitzungen samt Zeitgrenzen und optionalem Land. |
| `counters` | Zähler nach Spieler, Dimension, Schlüssel und Messwert, etwa Waffenschaden oder Gesamtkills. |
| `receipts` | Verarbeitete Ereignis-IDs und ihre Punktebelege; verhindert doppelte Verbuchung bei Wiederholungen. |
| `rounds` | Bereits beobachtete Kombinationen aus Runde und Spieler. |
| `metadata` | Serverzuordnung und allgemeine Angaben zur Verarbeitung. |

Eine bestätigte Steam-Identität ist dauerhaft. Solange sie fehlt, können Daten
an einer vorläufigen Sitzungsidentität hängen. `Resolve` führt diese Daten bei
späterer Authentifizierung zusammen. Eine spätere unvollständige Beobachtung
darf die bestätigte Identität nicht wieder zurückstufen.

`Count` erhöht einen einzelnen Zähler. `Apply` verwendet dafür lokale Hilfen:
`Add` verteilt Zähler auf Gesamt-, Map-, Sitzungs- und gegebenenfalls Waffenwerte;
`Award` verbucht erlaubte Punkteänderungen einschließlich Rundung und Untergrenze.
Eine gezählte Tötung kann die Ranglistenqualifikation voranbringen, auch wenn
für den Gegner keine reguläre Punktewertung möglich ist.

## Wo die gemeinsam genutzten Klassen liegen

Unter `Shared/` liegen die ursprünglich mit der Vollversion gemeinsam genutzten
Quelldateien. Dazu gehören Collectors, Ereignistypen, `CollectionContext`,
`EventQueue`, Diagnose-, GeoIP- und Chathelfer. Sie werden direkt mitkompiliert;
dieses Repository lässt sich ohne die Vollversion bauen.

Die Namen `SendAsync`, `StatsCommandClient` und die pfadartigen Abfragen stammen
aus dieser gemeinsamen Struktur. Im Lite-Projekt führen sie zu lokalen
SQLite-Zugriffen. Auch die JSON-Hülle für Punktebelege dient der Wiederverwendung
des gemeinsamen Filters; sie ist hier kein Netzwerkaufruf.

## Wo typische Änderungen hingehören

- **Punkte ändern:** Vorgaben in `RankingOptions`, Verbuchung in `LiteStore.Apply`.
- **Chattext ändern:** Vorlagen in `resources/config.jsonc`; Formatierung in
  `StatsCommandClient`, Verbindungsnachrichten in `ConnectChat`.
- **Eine Statistik abfragen:** Datenzugriff in `LiteQueries`, Ausgabe in
  `StatsCommandClient`, Befehlsregistrierung in `StatsChatCommands`.
- **Speicherprobleme untersuchen:** `EventDispatcher` für Warteschlange und
  Verluste, `SqliteStatsClient` für Wiederholungen, `LiteStore` für Transaktionen.

Aus dem Repository-Hauptordner starten die vorhandenen Prüfungen mit:

```powershell
dotnet run --project tests/finestats-lite.Tests.csproj -c Release
```

Sie prüfen unter anderem Punkte, Filter, Wiederholungen, Identitätswechsel,
Backups und konkurrierende SQLite-Zugriffe. Native Spiel-Callbacks und die
Chatdarstellung benötigen zusätzlich einen Test auf einem CS2-Server.
