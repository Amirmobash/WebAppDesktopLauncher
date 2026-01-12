# WebApp Desktop Launcher (Windows)

Ein leichter **Windows-Desktop-Client** für eine bestehende Web-Anwendung.

Statt den Browser zu öffnen und eine URL manuell aufzurufen, startet der Benutzer einfach eine einzige `.exe`.
Die Anwendung zeigt zuerst einen modernen **deutschen Ladebildschirm**, wartet bis das Backend erreichbar ist
(z. B. bei Render-Cold-Starts), und lädt anschließend die echte Web-UI in einem nativen Desktop-Fenster.

**Autor:** Amir Mobasheraghdam

---

## Funktionen

- 🖥️ Desktop-App-Feeling (ohne Browser-Chrome, ohne Adressleiste)
- 🌐 Lädt jede bestehende Web-Anwendung per konfigurierbarer URL
- ⏳ Deutscher Ladebildschirm während der Server startet
- 🚨 Deutscher Fehlerbildschirm bei Timeout / Offline
- ⚙️ Konfiguration über `appsettings.json`
- 📦 Single-File EXE Build via `dotnet publish` (Self-Contained)

---

## Voraussetzungen (Entwicklung)

- Windows 10/11
- .NET 8 SDK (oder Visual Studio 2022)
- WebView2 Runtime (meist bereits durch Microsoft Edge vorhanden)

---

## Projekt starten (Entwicklung)

Öffne eine PowerShell im Projektordner:

```powershell
cd .\src\WebAppDesktopLauncher\
dotnet restore
dotnet run
```

---

## Konfiguration

### Optional: Automatischer Login (Formular /login)

Wenn deine Anwendung (oder ein vorgeschalteter Schutz) eine **Login-Seite mit Benutzername + Passwort** anzeigt
(z. B. `/login`), kann der Launcher die Felder automatisch ausfüllen und absenden.

In `appsettings.json`:

```json
{
  "AutoLoginUser": "Amir",
  "AutoLoginPassword": "Amir"
}
```


### Optional: HTTP Basic Auth (z. B. Render Passwortschutz)

Wenn dein Hosting (z. B. Render) per **HTTP Basic Authentication** geschützt ist, kannst du Benutzername/Passwort
in der `appsettings.json` hinterlegen, damit der Login-Dialog nicht erscheint:

```json
{
  "BasicAuthUser": "DEIN_BENUTZER",
  "BasicAuthPassword": "DEIN_PASSWORT"
}
```


Datei: `src/WebAppDesktopLauncher/appsettings.json`

Wichtige Felder:

- `AppUrl` → URL deines Backends (Render, eigener Server, lokal usw.)
- `MaxWaitSeconds` → maximale Wartezeit (z. B. 300 = 5 Minuten)
- `PollSeconds` → Poll-Intervall
- `WindowTitle`, `Width`, `Height` → Fenster-Einstellungen

---

## Build: Single-File EXE (Distribution)

Im Projektordner `src/WebAppDesktopLauncher/` ausführen:

```powershell
dotnet publish -c Release -r win-x64 `
  /p:PublishSingleFile=true `
  /p:SelfContained=true `
  /p:IncludeNativeLibrariesForSelfExtract=true
```

Ergebnis:

```text
src/WebAppDesktopLauncher/bin/Release/net8.0-windows/win-x64/publish/
```

Dort liegt die **EXE**.

---

## Troubleshooting

### Es erscheint nur die Fehlerseite
Prüfe:

- Stimmt `AppUrl`?
- Ist die URL im normalen Browser erreichbar?
- Bei Render: Cold Start → `MaxWaitSeconds` erhöhen.

### WebView2 Probleme
In der Regel ist WebView2 Runtime bereits installiert.
Falls nicht: Microsoft WebView2 Runtime installieren (über Microsoft/Edge).

---

## Lizenz
Siehe `LICENSE`.
