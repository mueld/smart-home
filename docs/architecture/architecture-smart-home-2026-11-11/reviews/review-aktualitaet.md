---
review: aktualitaet
spine: ../ARCHITECTURE-SPINE.md
reviewer: Aktualitaetspruefer (unabhaengig)
date: 2026-11-11
---

# Review Aktualitaet – Architecture Spine smart-home Backend

## Gesamturteil

Die Architektur-Techniken (gemeinsame Verbindung und Transaktion mehrerer DbContexts, SaveChanges-Interceptor, C# 15 Unions, GUID v7, ARM64-Images) existieren und passen. Die **Stack-Tabelle ist aber teilweise veraltet oder unbestaetigt**: .NET 11 ist seit dem 2026-11-10 GA (die Spine nennt noch RC 3), OpenTelemetry .NET steht bei 1.21 statt 1.17, Testcontainers bei 4.16 statt 4.14, und der Npgsql-EF-Core-Provider 11 ist **noch nicht stabil** (letzte Version 11.0.0-rc.3). Ausserdem ist die Aussage aus dem Memlog, ein fehlender Union-Fall „bricht den Build“, ohne Zusatzkonfiguration falsch: Der Compiler meldet nur eine Warnung.

Pruefmethode: NuGet-Paketseiten, MCR- und Docker-Hub-Tag-Listen (per API am 2026-11-11 abgefragt), Microsoft Learn, npgsql.org, postgresql.org, aspire.dev, EF-Core-Quellcode auf GitHub.

## Befunde

### HIGH-1 – Npgsql-EF-Core-Provider 11 ist noch nicht stabil (Stack: „EF Core mit Npgsql-Provider 11.x“)

- **Befund:** EF Core 11.0.0 ist seit 2026-11-10 stabil auf NuGet. `Npgsql.EntityFrameworkCore.PostgreSQL` hat dagegen nur Vorabversionen 11.x; die neueste ist **11.0.0-rc.3 vom 2026-10-28**. Die neueste stabile Version ist 10.0.3, und die setzt EF Core `>= 10.0.4 && < 11.0.0` voraus, laeuft also **nicht** mit EF Core 11. Die Release Notes von Npgsql 11 sagen weiterhin „currently in development“. Die Spine behauptet „11.x“, ohne den Status zu nennen; im Memlog fehlt eine Versionspruefung des Providers.
- **Folgen fuer die Spine:** Der Provider 11 hebt die Standard-Zielversion von PostgreSQL auf 16 an (mit PostgreSQL 18 unproblematisch). Ausserdem gibt es Breaking Changes: transiente Fehler werden nicht mehr in `InvalidOperationException` verpackt; Serialisierungsfehler und Deadlocks werden zu `DbUpdateConcurrencyException`, das betrifft den globalen Exception Handler aus AD-10.
- **Fix:** In der Stack-Tabelle eintragen: „Npgsql EF Core 11.0.0-rc.x (GA ausstehend), auf 11.0.0 aktualisieren, sobald es erscheint“. Alternativ bis zum GA bewusst mit RC arbeiten und das als Risiko unter Deferred festhalten. EF Core und Npgsql-Provider in derselben Major-Version pinnen.
- **Quellen:** [NuGet Npgsql.EntityFrameworkCore.PostgreSQL](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL), [NuGet Microsoft.EntityFrameworkCore](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore), [Npgsql EF Core 11.0 Release Notes](https://www.npgsql.org/efcore/release-notes/11.0.html), [GitHub efcore.pg Releases](https://github.com/npgsql/efcore.pg/releases)

### HIGH-2 – .NET 11 ist GA, Spine und Projekt stehen auf RC 3 (Stack: „11.0 (im Projekt RC 3)“, `Backend.csproj`)

- **Befund:** EF Core 11.0.0 und `Microsoft.AspNetCore.OpenApi` 11.0.0 sind am 2026-11-10 als stabile Versionen erschienen. Microsoft hatte den GA von .NET 11 an der .NET Conf (ab 10. November 2026) angekuendigt. Die Seite „What's new in C# 15“ ist vom 2026-11-10 und nennt C# 15 als Standard-Sprachversion fuer `net11.0`. `Backend.csproj` referenziert noch `Microsoft.AspNetCore.OpenApi` `11.0.0-rc.3.26522.148`. Den GA-Blogpost selbst habe ich nicht gefunden; der GA ergibt sich aus den stabilen Paketen vom 10.11.
- **Fix:** In der Stack-Tabelle „.NET / ASP.NET Core 11.0 (GA 2026-11-10, STS bis 2028-11-09)“ eintragen und „im Projekt RC 3“ streichen. Im csproj auf `11.0.0` aktualisieren und das GA-SDK per `global.json` pinnen. Es ist ein STS-Release (Support bis 9. Nov. 2028); den Wechsel auf .NET 12 LTS unter Deferred aufnehmen.
- **Quellen:** [NuGet Microsoft.AspNetCore.OpenApi](https://www.nuget.org/packages/Microsoft.AspNetCore.OpenApi), [NuGet Microsoft.EntityFrameworkCore](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore), [Visual Studio Magazine – .NET 11 RC1, Launch im November](https://visualstudiomagazine.com/articles/2026/09/14/net-11-rc1-gets-go-live-support-ahead-of-november-launch.aspx), [Microsoft Learn – What's new in C# 15](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-15)

### MEDIUM-1 – Exhaustive switch expression bricht den Build nicht (AD-10)

- **Befund:** C# 15 Union Types sind bestaetigt. Sie sind seit .NET 11 kein Preview mehr, `LangVersion preview` ist nicht noetig (nur „Memory safety“ bleibt Preview). Ein fehlender Fall in einer switch expression erzeugt aber laut Sprachreferenz nur eine **Compiler-Warnung** (CS8509), keinen Fehler. Der Entscheid im Memlog („ein neuer Fehlerfall ohne Abbildung bricht den Build“) gilt damit nur mit Zusatzkonfiguration. Zudem ist eine generierte `union` ein **struct**: `default` hat `Value == null`, und eine switch expression ohne `null`-Arm wirft dann zur Laufzeit `SwitchExpressionException`, ohne dass der Compiler zwingend warnt. Die Sekundaerquellen aus dem Memlog („switch expressions statt statements, null-Arm“) sind damit durch die Primaerquelle bestaetigt.
- **Fix:** AD-10 ergaenzen: „`<WarningsAsErrors>CS8509;CS8655</WarningsAsErrors>` (oder `TreatWarningsAsErrors`) in `Directory.Build.props`. Die Abbildung enthaelt einen expliziten `null`-Arm (→ 500 bzw. Exception), aber keinen Discard.“
- **Quellen:** [Microsoft Learn – Union types (Sprachreferenz)](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/union), [Microsoft Learn – What's new in C# 15](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-15)

### MEDIUM-2 – OpenTelemetry .NET 1.17 ist veraltet (Stack)

- **Befund:** Die aktuelle stabile Version von `OpenTelemetry` und `OpenTelemetry.Extensions.Hosting` ist **1.21.0 (2026-11-11)**, davor 1.20.1 (2026-10-20). 1.17 ist mehrere Minor-Versionen alt; das Memlog hatte die Version selbst als „nicht direkt auf NuGet bestaetigt“ markiert. 1.21.0 listet `net10.0` und `net11.0` als kompatibel.
- **Fix:** Stack-Tabelle auf „OpenTelemetry .NET 1.21“ setzen (oder nur die Major-Version „1.x, jeweils aktuell“ angeben), da sich die Version monatlich aendert.
- **Quellen:** [NuGet OpenTelemetry](https://www.nuget.org/packages/OpenTelemetry), [NuGet OpenTelemetry.Extensions.Hosting](https://www.nuget.org/packages/OpenTelemetry.Extensions.Hosting)

### MEDIUM-3 – Synchrone I/O in Npgsql 11 und der „SavingChanges“-Hook (AD-6)

- **Befund:** Npgsql plant, in Version 11 synchrone I/O-APIs **standardmaessig mit einer Runtime-Exception** abzulehnen. Ein Schalter soll die synchrone I/O wieder erlauben; der Entscheid ist im Issue #5865 noch offen. Die Spine nennt nur den Hook „SavingChanges“. Domain-Event-Handler laufen ueber den Mediator in der Regel async, daher ist `SavingChangesAsync` der relevante Hook. Microsoft empfiehlt selbst, im synchronen `SavingChanges` zu werfen, wenn alle DB-I/O async sein soll.
- **Bestaetigt:** Im EF-Core-Quellcode (`DbContext.SaveChangesAsync`) laufen die Interceptoren (`SaveChangesStartingAsync`) **vor** `TryDetectChanges()`. Aenderungen, die Domain-Event-Handler im Interceptor am selben DbContext machen, werden deshalb im selben `SaveChanges` mitgeschrieben. Die Idee von AD-6 funktioniert also.
- **Fix:** AD-6 praezisieren: „Interceptor implementiert `SavingChangesAsync`; `SavingChanges` (sync) wirft `NotSupportedException`; im Code nur `SaveChangesAsync`“. Zusaetzlich: Der Interceptor haelt Zustand (Mediator, Scope), wird also pro Context-Instanz aus dem DI-Scope registriert (`AddDbContext((sp, o) => o.AddInterceptors(sp.GetRequiredService<…>()))`) und nicht als geteilte Instanz.
- **Quellen:** [Npgsql 11.0 Release Notes](https://www.npgsql.org/doc/release-notes/11.0.html), [npgsql/npgsql#5865](https://github.com/npgsql/npgsql/issues/5865), [Microsoft Learn – EF Core Interceptors](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors), [EF Core DbContext.cs](https://github.com/dotnet/efcore/blob/main/src/EFCore/DbContext.cs)

### MEDIUM-4 – Gemeinsame Transaktion mehrerer DbContexts: Technik bestaetigt, Randbedingungen fehlen (AD-14)

- **Befund:** Microsoft dokumentiert die „Cross-context transaction“ weiterhin (Stand 2026-08): Die Contexts muessen **dieselbe `DbConnection` und dieselbe `DbTransaction`** teilen, der beigetretene Context verwendet `Database.UseTransaction(Async)`. Das geht nur mit relationalen Providern, also auch mit Npgsql. Das Prinzip aus AD-14 stimmt damit. Zwei dokumentierte Randbedingungen fehlen in der Spine:
  1. **Verbindung muss von aussen kommen:** Bei `AddDbContext(... UseNpgsql(connectionString))` erzeugt jeder Context seine eigene Verbindung. Ohne explizite Massnahme teilen sie also nichts.
  2. **Retrying Execution Strategy** (`EnableRetryOnFailure`) wirft bei benutzergestarteten Transaktionen eine `InvalidOperationException`, ausser alles laeuft in `CreateExecutionStrategy().ExecuteAsync(...)`.
  Hinweis: Ist bereits eine Transaktion aktiv, legt EF bei `SaveChanges` automatisch einen Savepoint an. Das passt zur Regel, dass nur das aeusserste Behavior committet.
- **Fix:** AD-14 ergaenzen: „Eine scoped `NpgsqlConnection` (aus einer singleton `NpgsqlDataSource`) wird allen Modul-DbContexts per `UseNpgsql(DbConnection)` uebergeben; beigetretene Contexts rufen `UseTransactionAsync(…)` auf; keine Retrying Execution Strategy (transiente Fehler → 500 gemaess AD-10)“.
- **Quellen:** [Microsoft Learn – EF Core Transactions (Cross-context transaction, Savepoints)](https://learn.microsoft.com/en-us/ef/core/saving/transactions), [Microsoft Learn – Connection Resiliency (Execution strategies and transactions)](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency)

### LOW-1 – Testcontainers 4.14 veraltet, net11.0 nicht explizit als Target (Stack, Tests)

- **Befund:** Aktuell ist **4.16.0 (2026-10-08)**; 4.14.0 ist vom 2026-08-14. Die Pakete `Testcontainers` und `Testcontainers.PostgreSql` enthalten Builds fuer netstandard2.0/2.1, net8.0, net9.0 und net10.0, aber **kein net11.0-Target**. Auf .NET 11 wird das net10.0-Asset genutzt; das ist ueblich und erlaubt. Eine ausdrueckliche Kompatibilitaetsaussage fuer .NET 11 habe ich nicht gefunden. Lizenz: MIT.
- **Fix:** Stack auf 4.16 setzen. Im ersten Slice einen Smoke-Test (WebApplicationFactory + PostgreSQL-Container) als Nachweis der .NET-11-Kompatibilitaet einplanen. Das Testcontainers-Image auf dieselbe PostgreSQL-Version pinnen wie im Betrieb (`postgres:18.6`).
- **Quellen:** [NuGet Testcontainers](https://www.nuget.org/packages/Testcontainers), [NuGet Testcontainers.PostgreSql](https://www.nuget.org/packages/Testcontainers.PostgreSql)

### LOW-2 – Aspire Dashboard: Image-Pfad nicht festgelegt (Stack, Deferred)

- **Befund:** Bestaetigt: Aspire 13.6 (CLI 13.6.0 vom 2026-09-29). Auf MCR liegt `mcr.microsoft.com/aspire/dashboard` mit den Tags `13.6.5`, `13.6.5-arm64v8`, `latest`, ist also fuer den Raspberry Pi (ARM64) verfuegbar. Der alte Pfad `mcr.microsoft.com/dotnet/aspire-dashboard` wird laut Doku nur noch fuer Aspire-13-Servicing gepflegt. Die optionale Persistenz ist bestaetigt (`ASPIRE_DASHBOARD_PERSISTENCE_MODE`, Volume unter `/home/app`). Ohne Persistenz gehen die Telemetriedaten bei einem Neustart verloren. Port-Mapping: OTLP gRPC 18889, HTTP 18890 im Container.
- **Fix:** Stack: „Aspire Dashboard `mcr.microsoft.com/aspire/dashboard:13.6` (arm64v8 verfuegbar)“. Deferred: Persistenz-Variable und Volume nennen.
- **Quellen:** [aspire.dev – Standalone dashboard](https://aspire.dev/dashboard/standalone/), MCR-Tag-Liste `https://mcr.microsoft.com/v2/aspire/dashboard/tags/list` (abgefragt 2026-11-11)

### LOW-3 – GUID v7 und TimeProvider (AD-11, AD-12)

- **Befund:** `Guid.CreateVersion7()` gibt es seit .NET 9, auch in .NET 11. Die parameterlose Variante liest aber intern `DateTimeOffset.UtcNow` und umgeht damit die Regel aus AD-11, dass Zeit nur aus `TimeProvider` kommt. Es gibt die Ueberladung `CreateVersion7(DateTimeOffset)`.
- **Fix:** AD-12: „IDs mit `Guid.CreateVersion7(timeProvider.GetUtcNow())`“.
- **Quelle:** [Microsoft Learn – Guid.CreateVersion7](https://learn.microsoft.com/en-us/dotnet/api/system.guid.createversion7)

### LOW-4 – FluentValidation 12.2: bestaetigt, Hinweis zu Target und Lizenz (Stack, AD-5)

- **Befund:** NuGet: **12.2.0 vom 2026-10-10, Lizenz Apache-2.0** (das Memlog hatte die Lizenz als unbestaetigt markiert; jetzt bestaetigt). Das Paket enthaelt nur einen `net8.0`-Build und laeuft damit auf .NET 11. Anders als bei MediatR gibt es keinen Lizenzwechsel. Der Hinweis in AD-5 („Lizenzabhaengigkeit verhindern“) ist damit auch fuer FluentValidation erfuellt.
- **Fix:** Keiner noetig; optional „(Apache-2.0)“ in der Stack-Tabelle ergaenzen. Version nicht auf eine Minor-Version festnageln.
- **Quelle:** [NuGet FluentValidation](https://www.nuget.org/packages/FluentValidation)

## Bestaetigt ohne Befund

| Eintrag | Pruefergebnis | Quelle |
| --- | --- | --- |
| PostgreSQL 18 | Aktuell 18.6 (2026-08-13); PostgreSQL 19 erst RC 1 (2026-10-15). Docker-Image `postgres:18` mit `arm64v8` vorhanden. PostgreSQL 14 ist ab 2026-11-12 EOL (nicht betroffen). | [postgresql.org](https://www.postgresql.org/), Docker-Hub-Tags `library/postgres` (abgefragt 2026-11-11) |
| Eclipse Mosquitto 2.1 | 2.1.0 (Jan. 2026), aktuell 2.1.2. Image `eclipse-mosquitto:2.1` / `2.1.2-alpine` mit `arm64v8`, zuletzt neu gebaut am 2026-11-06. Parallel wird 2.0.22 gepflegt. Empfehlung: Tag `2.1` oder `2.1.2` pinnen statt `latest`. | [Eclipse Projects – Mosquitto 2.1](https://projects.eclipse.org/projects/iot.mosquitto/releases/2.1/review), Docker-Hub-Tags `library/eclipse-mosquitto` (abgefragt 2026-11-11) |
| Basis-Image `mcr.microsoft.com/dotnet/aspnet:11.0` (csproj) | Vorhanden, inkl. `11.0-*-arm64v8` | MCR-Tag-Liste `dotnet/aspnet` (abgefragt 2026-11-11) |
| C# 15 Union Types | In .NET 11 stabil, Standard-Sprachversion fuer `net11.0`, kein Preview-Flag noetig | [What's new in C# 15](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-15) |
| SaveChanges-Interceptor | `ISaveChangesInterceptor` mit `SavingChanges`/`SavingChangesAsync` dokumentiert; Interceptoren werden pro DbContext-Instanz registriert | [EF Core Interceptors](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors) |

## Nicht geprueft

- Docker Compose v2 (Status trivial, keine Version im Projekt relevant).
- Kompatibilitaet des Python-Hilfsdiensts und von Tailscale auf dem Raspberry Pi (nicht Teil der Stack-Tabelle).
- Telegram Bot API (keine Version festgelegt).
