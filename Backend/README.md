# Backend

## EF Core Migrationen hinzufuegen

Alle Befehle in diesem Abschnitt werden aus dem Verzeichnis `backend` ausgefuehrt.

Falls `dotnet ef` lokal als Tool verwendet wird, zuerst die Tools wiederherstellen:

```powershell
dotnet tool restore
```

```powershell
dotnet ef migrations add <MigrationName> --project .\src\Modules\Home\Infrastructure\ --startup-project .\src\Host\Backend.Host.csproj --context HomeDbContext --output-dir .\src\Modules\Home\Infrastructure\Migrations
```

Beispiel:

```powershell
dotnet ef migrations add InitialMigration --project .\src\Modules\Home\Infrastructure\ --startup-project .\src\Host\Backend.Host.csproj --context HomeDbContext --output-dir .\src\Modules\Home\Infrastructure\Migrations
```

Die erzeugten Migrationen werden jeweils im Infrastrukturprojekt unter `Persistence/Migrations` abgelegt.

## EF Core Migrationen entfernen

Alle Befehle in diesem Abschnitt werden ebenfalls aus dem Verzeichnis `backend` ausgefuehrt.

```powershell
dotnet ef migrations remove --project .\Infrastructure\ --startup-project .\VerfahrensRegistry\VerfahrensRegistry.csproj --context VerfahrensRegistryDbContext
```
