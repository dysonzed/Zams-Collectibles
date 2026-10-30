# Uploads (or updates) Zam's Collectibles on the Steam Workshop with steamcmd, from the installed
# mod folder described in workshop.vdf. Run it in its own window. The Steam login is asked for in a
# Windows sign-in box (paste works there) and handed straight to steamcmd; it is not written to
# disk. steamcmd then asks for the Steam Guard code in this window. It writes the new item's id
# back into workshop.vdf; keep it, and later runs update the same item.
$steamcmd = "C:\steamcmd\steamcmd.exe"
$vdf = Join-Path $PSScriptRoot "workshop.vdf"

Write-Host "Zam's Collectibles: Steam Workshop upload" -ForegroundColor Cyan
Write-Host "Uploading the mod folder listed in $vdf"
$cred = Get-Credential -UserName "canadianz3d" -Message "Steam login for the Workshop upload"
if ($null -eq $cred) { Write-Host "Cancelled."; Read-Host "Press Enter to close"; exit }
$password = $cred.GetNetworkCredential().Password
& $steamcmd +login $cred.UserName $password +workshop_build_item $vdf +quit
$password = $null
Write-Host ""
Write-Host "Done (exit code $LASTEXITCODE). If it worked, the item id is now in workshop.vdf." -ForegroundColor Cyan
Read-Host "Press Enter to close"
