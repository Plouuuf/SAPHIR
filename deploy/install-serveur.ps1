# =====================================================================================
#  SAPHIR — installation / mise à jour du serveur sur le VPS Windows
#
#  À lancer dans PowerShell ouvert en administrateur :
#    irm https://raw.githubusercontent.com/Plouuuf/SAPHIR/main/deploy/install-serveur.ps1 | iex
#
#  Première fois : télécharge le serveur, crée la base MariaDB « saphir » et son utilisateur,
#  écrit appsettings.Local.json (clé, Discord, base), ouvre le port 8091, crée la tâche
#  planifiée « SAPHIR Server » (démarrage automatique) et lance le serveur.
#  Fois suivantes : met simplement le serveur à jour, les réglages sont conservés.
# =====================================================================================

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ProgressPreference = 'SilentlyContinue'

$Dossier   = 'C:\SAPHIR'
$Tache     = 'SAPHIR Server'
$Port      = 8091
$Depot     = 'Plouuuf/SAPHIR'
$Reglages  = Join-Path $Dossier 'appsettings.Local.json'
$AdresseIp = '46.105.78.32'

function Etape($texte) { Write-Host "`n==> $texte" -ForegroundColor Cyan }
function Ok($texte)    { Write-Host "    $texte" -ForegroundColor Green }
function Info($texte)  { Write-Host "    $texte" }
function Alerte($texte){ Write-Host "    $texte" -ForegroundColor Yellow }

function NouveauSecret([int]$longueur) {
    $car = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'.ToCharArray()
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $octets = New-Object byte[] $longueur
    $rng.GetBytes($octets)
    -join ($octets | ForEach-Object { $car[$_ % $car.Length] })
}

function Installer {
    $admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $admin) {
        Write-Host "Ouvre PowerShell en administrateur (clic droit > Exécuter en tant qu'administrateur) puis relance la commande." -ForegroundColor Red
        return
    }

    # ----- 1. Dernière version publiée -----
    Etape "Recherche de la dernière version de SAPHIR"
    $release = Invoke-RestMethod "https://api.github.com/repos/$Depot/releases/latest" -Headers @{ 'User-Agent' = 'SAPHIR-install' }
    $paquet = $release.assets | Where-Object { $_.name -eq 'SAPHIR-server-win-x64.zip' } | Select-Object -First 1
    if (-not $paquet) { throw "Paquet serveur introuvable dans la version $($release.tag_name)." }
    Ok "$($release.name) ($($release.tag_name))"

    $zip = Join-Path $env:TEMP 'SAPHIR-server.zip'
    Info "Téléchargement…"
    Invoke-WebRequest $paquet.browser_download_url -OutFile $zip -UseBasicParsing

    # ----- 2. Arrêt de l'ancien serveur et copie -----
    Etape "Installation dans $Dossier"
    if (Get-ScheduledTask -TaskName $Tache -ErrorAction SilentlyContinue) { Stop-ScheduledTask -TaskName $Tache }
    Get-Process SAPHIR.Server -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep 2
    New-Item -ItemType Directory -Force -Path $Dossier | Out-Null
    Expand-Archive $zip $Dossier -Force
    Remove-Item $zip -Force
    Ok "Fichiers copiés."

    # ----- 3. Réglages (première fois seulement) -----
    if (Test-Path $Reglages) {
        Etape "Réglages existants conservés ($Reglages)"
    } else {
        Etape "Première installation : réglages"
        $reglagesJson = PremiereConfiguration
        if (-not $reglagesJson) { return }
        [IO.File]::WriteAllText($Reglages, $reglagesJson, (New-Object Text.UTF8Encoding $false))
        Ok "appsettings.Local.json écrit."
    }

    # ----- 4. Pare-feu -----
    Etape "Pare-feu : port $Port"
    if (-not (Get-NetFirewallRule -DisplayName 'SAPHIR Server' -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName 'SAPHIR Server' -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
        Ok "Règle créée."
    } else { Ok "Règle déjà présente." }

    # ----- 5. Tâche planifiée (démarrage avec Windows, relance en cas d'arrêt) -----
    Etape "Tâche planifiée « $Tache »"
    $exe = Join-Path $Dossier 'SAPHIR.Server.exe'
    $action    = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $Dossier
    $trigger   = New-ScheduledTaskTrigger -AtStartup
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                    -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -StartWhenAvailable
    Register-ScheduledTask -TaskName $Tache -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
    Start-ScheduledTask -TaskName $Tache
    Ok "Serveur lancé."

    # ----- 6. Vérification -----
    Etape "Vérification"
    $etat = $null
    for ($i = 0; $i -lt 20 -and -not $etat; $i++) {
        Start-Sleep 2
        try { $etat = Invoke-RestMethod "http://127.0.0.1:$Port/health" -TimeoutSec 3 } catch { }
    }
    if (-not $etat) {
        Alerte "Le serveur ne répond pas. Lance-le à la main pour voir l'erreur :"
        Alerte "  cd $Dossier ; .\SAPHIR.Server.exe"
        return
    }
    $version = try { (Invoke-RestMethod "http://127.0.0.1:$Port/api/version" -TimeoutSec 3).version } catch { '?' }
    Ok "En ligne : $version"
    if ($etat.database) { Ok "Base MariaDB : connectée, tables à jour." }
    else { Alerte "Base MariaDB : injoignable. Vérifie la chaîne Database:Saphir dans $Reglages puis relance la tâche." }

    Write-Host "`nSAPHIR est installé. Adresse publique : http://${AdresseIp}:$Port/health" -ForegroundColor Green
    Write-Host "Pour mettre à jour plus tard : relance la même commande." -ForegroundColor Green
}

# Crée la base et rédige appsettings.Local.json. Renvoie le JSON, ou $null si l'installation doit s'arrêter.
function PremiereConfiguration {
    # --- Discord : on reprend l'application d'ARGOS si elle est sur ce VPS ---
    $clientId = $null; $clientSecret = $null
    foreach ($f in 'C:\ARGOS\appsettings.Local.json', 'C:\ARGOS\appsettings.json') {
        if (-not $clientId -and (Test-Path $f)) {
            try {
                $argos = Get-Content $f -Raw -Encoding UTF8 | ConvertFrom-Json
                if ($argos.Discord.ClientId -and $argos.Discord.ClientSecret) {
                    $clientId = $argos.Discord.ClientId; $clientSecret = $argos.Discord.ClientSecret
                    Ok "Application Discord d'ARGOS reprise ($f)."
                }
            } catch { }
        }
    }
    if (-not $clientId) {
        Info "Application Discord (portail développeur Discord > OAuth2) :"
        $clientId = Read-Host "    Client ID"
        $clientSecret = Read-Host "    Client Secret"
    }

    # --- MariaDB ---
    $mysql = @(
        'C:\Program Files\MariaDB*\bin\mariadb.exe', 'C:\Program Files\MariaDB*\bin\mysql.exe',
        'C:\Program Files\MySQL\*\bin\mysql.exe', 'C:\xampp\mysql\bin\mysql.exe',
        'C:\laragon\bin\mysql\*\bin\mysql.exe', 'C:\wamp64\bin\mysql\*\bin\mysql.exe'
    ) | ForEach-Object { Get-Item $_ -ErrorAction SilentlyContinue } | Select-Object -First 1
    if (-not $mysql) {
        $cmd = Get-Command mariadb, mysql -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($cmd) { $mysql = Get-Item $cmd.Source }
    }
    if (-not $mysql) {
        Alerte "Client MariaDB (mysql.exe) introuvable."
        $chemin = Read-Host "    Chemin complet de mysql.exe ou mariadb.exe"
        $mysql = Get-Item $chemin
    }
    Ok "Client MariaDB : $($mysql.FullName)"

    $motDePasseBase = NouveauSecret 24
    $sql = @"
CREATE DATABASE IF NOT EXISTS saphir CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS 'saphir'@'localhost' IDENTIFIED BY '$motDePasseBase';
CREATE USER IF NOT EXISTS 'saphir'@'127.0.0.1' IDENTIFIED BY '$motDePasseBase';
ALTER USER 'saphir'@'localhost' IDENTIFIED BY '$motDePasseBase';
ALTER USER 'saphir'@'127.0.0.1' IDENTIFIED BY '$motDePasseBase';
GRANT ALL PRIVILEGES ON saphir.* TO 'saphir'@'localhost';
GRANT ALL PRIVILEGES ON saphir.* TO 'saphir'@'127.0.0.1';
FLUSH PRIVILEGES;
"@
    $fichierSql = Join-Path $env:TEMP 'saphir-init.sql'
    [IO.File]::WriteAllText($fichierSql, $sql, (New-Object Text.UTF8Encoding $false))

    $reussi = $false
    for ($essai = 1; $essai -le 3 -and -not $reussi; $essai++) {
        $mdp = Read-Host "    Mot de passe « root » de MariaDB (Entrée si aucun)" -AsSecureString
        $env:MYSQL_PWD = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($mdp))
        cmd /c "`"$($mysql.FullName)`" -u root -h 127.0.0.1 -P 3306 < `"$fichierSql`" 2>&1" | ForEach-Object { Info $_ }
        $reussi = ($LASTEXITCODE -eq 0)
        if (-not $reussi) { Alerte "Échec (mot de passe root incorrect ?)." }
    }
    Remove-Item Env:MYSQL_PWD -ErrorAction SilentlyContinue
    Remove-Item $fichierSql -Force
    if (-not $reussi) {
        Write-Host "Base non créée : installation arrêtée. Relance la commande avec le bon mot de passe root." -ForegroundColor Red
        return $null
    }
    Ok "Base « saphir » et utilisateur « saphir » prêts."

    $reglages = [ordered]@{
        SAPHIR   = [ordered]@{ AccessKey = (NouveauSecret 48); AdminDiscordIds = @('560037927695024139') }
        Discord  = [ordered]@{ ClientId = $clientId; ClientSecret = $clientSecret; PublicBaseUrl = "http://${AdresseIp}:$Port" }
        Database = [ordered]@{ Saphir = "Server=127.0.0.1;Port=3306;Database=saphir;User ID=saphir;Password=$motDePasseBase" }
    }
    return ($reglages | ConvertTo-Json -Depth 5)
}

try { Installer } catch { Write-Host "`nErreur : $($_.Exception.Message)" -ForegroundColor Red }
