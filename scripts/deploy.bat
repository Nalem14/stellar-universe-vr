@echo off
setlocal EnableExtensions EnableDelayedExpansion
rem Publish Builds\Android, Windows, macOS, Linux to itch.io via Butler.
rem Target: game 861522. A channel is skipped when its folder has nothing to ship.
rem The itch build number is automatic. --userversion is the label players see:
rem ProjectSettings bundleVersion, unless a version is passed on the command line.
rem   scripts\deploy.bat
rem   scripts\deploy.bat android
rem   scripts\deploy.bat 1.1
rem   scripts\deploy.bat android 1.1

cd /d "%~dp0.."
set "GAME_ID=861522"
set "FAILED=0"
set "PUSHED=0"
set "MATCHED=0"
set "WANT="
set "VERSION="
set "ARG1=%~1"
set "ARG2=%~2"

if /i "%ARG1%"=="android" set "WANT=%ARG1%"
if /i "%ARG1%"=="windows" set "WANT=%ARG1%"
if /i "%ARG1%"=="macos" set "WANT=%ARG1%"
if /i "%ARG1%"=="linux" set "WANT=%ARG1%"
if /i "%ARG1%"=="Android" set "WANT=%ARG1%"
if /i "%ARG1%"=="Windows" set "WANT=%ARG1%"
if /i "%ARG1%"=="macOS" set "WANT=%ARG1%"
if /i "%ARG1%"=="Linux" set "WANT=%ARG1%"

if defined WANT (
  set "VERSION=%ARG2%"
  if not "%~3"=="" (
    echo Usage : scripts\deploy.bat [canal] [version]
    exit /b 1
  )
) else if not "%ARG1%"=="" (
  echo %ARG1%| findstr /r "^[0-9]" >nul
  if errorlevel 1 (
    echo Canal inconnu : %ARG1% ^(android, windows, macos, linux^)
    exit /b 1
  )
  set "VERSION=%ARG1%"
  if not "%ARG2%"=="" (
    echo Usage : scripts\deploy.bat [canal] [version]
    exit /b 1
  )
)

if not defined VERSION (
  for /f "tokens=1,* delims=:" %%A in ('findstr /c:"  bundleVersion:" ProjectSettings\ProjectSettings.asset') do (
    if not defined VERSION set "VERSION=%%B"
  )
)
if defined VERSION set "VERSION=%VERSION: =%"
if not defined VERSION (
  echo Version manquante. Passe-la en argument, ou renseigne bundleVersion.
  exit /b 1
)

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
  set "SKIP=0"
  if /i "%%F"==".gitkeep" set "SKIP=1"
  if /i "%%F"==".DS_Store" set "SKIP=1"
  echo %%F | findstr /i "BurstDebugInformation_DoNotShip BackUpThisFolder_ButDontShipItWithYourGame" >nul && set "SKIP=1"
  if "!SKIP!"=="0" set "NONEMPTY=1"
)
if "!NONEMPTY!"=="0" (
  echo !DIR! : vide, rien a envoyer.
  exit /b 0
)

echo Envoi de !DIR! vers !GAME_ID!:!CHANNEL! ^(!VERSION!^)
"!BUTLER!" push "!DIR!" "!GAME_ID!:!CHANNEL!" --userversion "!VERSION!" --assume-yes --fix-permissions --if-changed --ignore ".DS_Store" --ignore "**/.DS_Store" --ignore ".gitkeep" --ignore "*BurstDebugInformation_DoNotShip" --ignore "*BurstDebugInformation_DoNotShip/**" --ignore "*BackUpThisFolder_ButDontShipItWithYourGame" --ignore "*BackUpThisFolder_ButDontShipItWithYourGame/**"
if errorlevel 1 (
  echo Echec : !GAME_ID!:!CHANNEL!
  set "FAILED=1"
) else (
  set /a PUSHED+=1
)
exit /b 0
