# SicherungsWächter

Windows-Programm, das ausgewählte Ordner oder Dateien automatisch auf eine andere Festplatte sichert – sofort, wenn etwas neu hinzukommt, geändert, umbenannt oder gelöscht wird.

- Echtzeit-Überwachung (FileSystemWatcher) + regelmäßige Komplettprüfung
- Ältere Fassungen in `_Versionen`, gelöschte Dateien in `_Gelöscht` (Aufbewahrung einstellbar)
- Festplatte abgesteckt → Änderungen werden nachgeholt
- Wiederherstellen von Versionen/gelöschten Dateien, Autostart, Tray-Symbol, hell/dunkel
- Update-Knopf: holt die neueste Version aus den GitHub-Releases, prüft SHA-256 und Selbsttest, tauscht die EXE aus und startet neu (keine Neuinstallation)

## Installieren (ohne SmartScreen-Sperre)
PowerShell öffnen und einfügen:
```
irm https://raw.githubusercontent.com/Dodovking/SicherungsWaechter/main/install.ps1 | iex
```
Installiert nach `%LOCALAPPDATA%\SicherungsWaechter`, legt Verknüpfungen auf Desktop und im Startmenü an und startet das Programm. Derselbe Befehl aktualisiert auch.

Alternativ: https://github.com/Dodovking/SicherungsWaechter/releases/latest – `SicherungsWaechter.exe` herunterladen (Eigenschaften → „Zulassen“, falls Windows blockiert).

## Aufbau
- `src/SicherungsWaechter.Core` – Sync-Engine, Überwachung, Einstellungen (plattformunabhängig, getestet)
- `src/SicherungsWaechter.Gui` – WinForms-Oberfläche (Startargumente: `--minimiert`, `--daten <ordner>`, `--design hell|dunkel`)
- `tests/SicherungsWaechter.Tests` – xUnit

## Bauen / Veröffentlichen
Neue Version: `<Version>` in `src/SicherungsWaechter.Gui/SicherungsWaechter.Gui.csproj` erhöhen, dann `tools/release.sh "Was ist neu"`.
Test-Updatequelle: `--update-quelle <URL zu einer Release-JSON>`.

```
dotnet test
dotnet publish src/SicherungsWaechter.Gui -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o artifacts/publish
```
Einstellungen/Protokoll: `%APPDATA%\SicherungsWaechter`.
