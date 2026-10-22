# SicherungsWaechter installieren/aktualisieren:
#   irm https://raw.githubusercontent.com/Dodovking/SicherungsWaechter/main/install.ps1 | iex
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { }

$ae = [char]0xE4
$name = "SicherungsW${ae}chter"
$dir = Join-Path $env:LOCALAPPDATA 'SicherungsWaechter'
$exe = Join-Path $dir 'SicherungsWaechter.exe'
$tmp = "$exe.download"
$base = 'https://github.com/Dodovking/SicherungsWaechter/releases/latest/download'

try {
    Write-Host ""
    Write-Host "$name wird installiert ..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $dir | Out-Null

    Write-Host "Lade das Programm herunter (ca. 65 MB) ..."
    Invoke-WebRequest -Uri "$base/SicherungsWaechter.exe" -OutFile $tmp -UseBasicParsing
    $hashFile = (Invoke-WebRequest -Uri "$base/SicherungsWaechter.exe.sha256" -UseBasicParsing).Content
    if ($hashFile -is [byte[]]) { $hashFile = [Text.Encoding]::ASCII.GetString($hashFile) }
    $expected = ($hashFile.Trim() -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash -Algorithm SHA256 -Path $tmp).Hash.ToLowerInvariant()
    if ($expected -ne $actual) { throw "Die heruntergeladene Datei ist besch${ae}digt (Pr$([char]0xFC)fsumme stimmt nicht)." }

    $running = Get-Process -Name 'SicherungsWaechter' -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "Beende die laufende Version ..."
        $running | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
    Move-Item -Force -Path $tmp -Destination $exe
    Unblock-File -Path $exe -ErrorAction SilentlyContinue

    $shell = New-Object -ComObject WScript.Shell
    foreach ($folder in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) {
        $lnk = $shell.CreateShortcut((Join-Path $folder "$name.lnk"))
        $lnk.TargetPath = $exe
        $lnk.WorkingDirectory = $dir
        $lnk.IconLocation = "$exe,0"
        $lnk.Description = 'Automatische Sicherung auf eine andere Festplatte'
        $lnk.Save()
    }

    Start-Process -FilePath $exe -WorkingDirectory $dir
    Write-Host ""
    Write-Host "Fertig! $name l${ae}uft jetzt. Verkn$([char]0xFC)pfung liegt auf dem Desktop und im Startmen$([char]0xFC)." -ForegroundColor Green
    Write-Host "Installiert in: $dir"
}
catch {
    if (Test-Path $tmp) { Remove-Item -Force $tmp -ErrorAction SilentlyContinue }
    Write-Host ""
    Write-Host "Installation fehlgeschlagen: $($_.Exception.Message)" -ForegroundColor Red
}
