@echo off
REM Launch the study from anywhere, on Windows.
REM
REM   study            ask which mode
REM   study demo       film
REM   study study      run a participant
REM   study pull       copy demo clips off the headset
REM
REM The Windows twin of the `study` bash script beside it. The name is shared on
REM purpose: cmd resolves a path ending in `study` to `study.cmd` through
REM PATHEXT, so the command a researcher types and pastes is character for
REM character the same on both machines. Two files, one instruction.
REM
REM Resolves its own location, so it does not care where it is run from.

setlocal
cd /d "%~dp0Server" 2>nul
if errorlevel 1 (
    echo Could not find the Server folder next to this script.
    echo Run this file from inside the cloned repository.
    pause
    exit /b 1
)

where node >nul 2>&1
if errorlevel 1 (
    echo.
    echo   Node.js is not installed, or not on PATH.
    echo   Install the LTS build from https://nodejs.org, then open a NEW
    echo   terminal window - this one will not see it.
    echo.
    pause
    exit /b 1
)

REM A fresh clone has no node_modules. The bash script assumes they are already
REM there because the machine it grew up on had them; a second researcher
REM cloning for the first time does not, and a bare "cannot find module" is a
REM poor first impression of a study tool.
if not exist "node_modules" (
    echo.
    echo   First run - installing what the server needs. Once only, a minute or two.
    echo.
    call npm install
    if errorlevel 1 (
        echo.
        echo   npm install failed. Check the internet connection and try again.
        echo.
        pause
        exit /b 1
    )
    echo.
)

REM node directly rather than through npm: npm prints two lines of its own
REM before anything happens, and the first thing on screen should be the mode
REM question.
if "%~1"=="" (
    node scripts\study.js
) else if /i "%~1"=="demo" (
    node scripts\study.js --demo
) else if /i "%~1"=="study" (
    node scripts\study.js --study
) else if /i "%~1"=="pull" (
    node scripts\demo.js pull
) else (
    echo Usage:  study            ask which mode
    echo         study demo       go straight to filming
    echo         study study      go straight to a participant session
    echo         study pull       copy demo clips off the headset
    exit /b 1
)

exit /b %ERRORLEVEL%
