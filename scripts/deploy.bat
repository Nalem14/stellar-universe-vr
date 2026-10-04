@echo off
setlocal EnableExtensions EnableDelayedExpansion
rem Publish Builds\Android, Windows, macOS, Linux to itch.io via Butler.
rem Target: game 861522. A channel is skipped when its folder has nothing to ship.
rem   scripts\deploy.bat
rem   scripts\deploy.bat android

cd /d "%~dp0.."
set "GAME_ID=861522"
set "WANT=%~1"
set "FAILED=0"
set "PUSHED=0"
set "MATCHED=0"

set "BUTLER="
where butler >nul 2>&1 && set "BUTLER=butler"
if not defined BUTLER if exist "%APPDATA%\itch\apps\butler\butler.exe" set "BUTLER=%APPDATA%\itch\apps\butler\butler.exe"
if not defined BUTLER if exist "%LOCALAPPDATA%\itch\apps\butler\butler.exe" set "BUTLER=%LOCALAPPDATA%\itch\apps\butler\butler.exe"
if not defined BUTLER (
  echo Butler introuvable. Installe-le avec l'app itch.io, ou ajoute-le au PATH.
  exit /b 1
)

call :one "Builds\Android" android
call :one "Builds\Windows" windows
call :one "Builds\macOS" macos
call :one "Builds\Linux" linux

if "!PUSHED!"=="0" if "!FAILED!"=="0" if not "!WANT!"=="" if "!MATCHED!"=="0" (
  echo Canal inconnu : !WANT! ^(android, windows, macos, linux^)
  exit /b 1
)
exit /b !FAILED!

:one
set "DIR=%~1"
set "CHANNEL=%~2"
if not "!WANT!"=="" if /i not "!WANT!"=="!CHANNEL!" if /i not "!WANT!"=="%~nx1" exit /b 0
set "MATCHED=1"
if not exist "!DIR!\" mkdir "!DIR!"

set "NONEMPTY=0"
for /f "delims=" %%F in ('dir /b /a "!DIR!" 2^>nul') do (
  if /i not "%%F"==".gitkeep" if /i not "%%F"==".DS_Store" set "NONEMPTY=1"
)
if "!NONEMPTY!"=="0" (
  echo !DIR! : vide, rien a envoyer.
  exit /b 0
)

echo Envoi de !DIR! vers !GAME_ID!:!CHANNEL!
"!BUTLER!" push "!DIR!" "!GAME_ID!:!CHANNEL!" --assume-yes --fix-permissions --if-changed --ignore ".DS_Store" --ignore ".gitkeep" --ignore "*BurstDebugInformation_DoNotShip"
if errorlevel 1 (
  echo Echec : !GAME_ID!:!CHANNEL!
  set "FAILED=1"
) else (
  set /a PUSHED+=1
)
exit /b 0
