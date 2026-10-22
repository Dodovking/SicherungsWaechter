# SicherungsWächter

Windows-Programm, das ausgewählte Ordner oder Dateien automatisch auf eine andere Festplatte sichert – sofort, wenn etwas neu hinzukommt, geändert, umbenannt oder gelöscht wird.

- Echtzeit-Überwachung (FileSystemWatcher) + regelmäßige Komplettprüfung
- Ältere Fassungen in `_Versionen`, gelöschte Dateien in `_Gelöscht` (Aufbewahrung einstellbar)
- Festplatte abgesteckt → Änderungen werden nachgeholt
- Wiederherstellen von Versionen/gelöschten Dateien, Autostart, Tray-Symbol, hell/dunkel

## Aufbau
- `src/SicherungsWaechter.Core` – Sync-Engine, Überwachung, Einstellungen (plattformunabhängig, getestet)
- `src/SicherungsWaechter.Gui` – WinForms-Oberfläche (Startargumente: `--minimiert`, `--daten <ordner>`, `--design hell|dunkel`)
- `tests/SicherungsWaechter.Tests` – xUnit

## Bauen
```
dotnet test
dotnet publish src/SicherungsWaechter.Gui -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o artifacts/publish
```
Einstellungen/Protokoll: `%APPDATA%\SicherungsWaechter`.
