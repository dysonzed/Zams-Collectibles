@echo off
rem Opens the card art importer (see card-art.ps1).
start "" powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0card-art.ps1"
