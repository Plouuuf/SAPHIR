# =====================================================================================
#  SAPHIR — application d'une mise à jour du serveur (lancée par la tâche « SAPHIR Mise a jour »).
#  Le serveur télécharge lui-même data\updates\serveur.zip puis déclenche cette tâche (SYSTEM).
#  Journal : data\updates\maj-serveur.log
# =====================================================================================
$ErrorActionPreference = 'Stop'
$Dossier = Split-Path -Parent $MyInvocation.MyCommand.Path
$Zip     = Join-Path $Dossier 'data\updates\serveur.zip'
$Journal = Join-Path $Dossier 'data\updates\maj-serveur.log'
$Tache   = 'SAPHIR Server'

function Log($t) { Add-Content -Path $Journal -Value ("{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $t) -Encoding UTF8 }

try {
    if (-not (Test-Path $Zip)) { Log "Aucun paquet serveur.zip : rien à faire."; return }
    Log "Mise à jour du serveur : arrêt."
    Start-Sleep 3
    Stop-ScheduledTask -TaskName $Tache -ErrorAction SilentlyContinue
    Get-Process SAPHIR.Server -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep 3

    $sauvegarde = Join-Path $env:TEMP 'saphir-sauvegarde'
    if (Test-Path $sauvegarde) { Remove-Item $sauvegarde -Recurse -Force }
    New-Item -ItemType Directory -Path $sauvegarde | Out-Null
    Get-ChildItem $Dossier -File | Copy-Item -Destination $sauvegarde

    try {
        Expand-Archive $Zip $Dossier -Force
        Log "Fichiers installés."
    } catch {
        Log ("Échec de l'installation : " + $_.Exception.Message + " — restauration de la version précédente.")
        Get-ChildItem $sauvegarde -File | Copy-Item -Destination $Dossier -Force
    }
    Remove-Item $Zip -Force -ErrorAction SilentlyContinue
} catch {
    Log ("ERREUR : " + $_.Exception.Message)
} finally {
    Start-ScheduledTask -TaskName $Tache
    Log "Serveur relancé."
}
