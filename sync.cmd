@echo off
setlocal enabledelayedexpansion
REM One-command sync with GitHub, for working across two machines.
REM
REM   sync              pull, commit everything, push
REM   sync "message"    same, with your own commit message
REM   sync --pull       pull only, commit and push nothing
REM
REM The Windows twin of the `sync` script beside it. Same steps, same guards,
REM same refusals, so the instruction given to either researcher is identical.
REM
REM Why this is not a background auto-sync is explained at the top of `sync`.
REM The short version: Logs/ holds participant recordings, Unity scenes conflict
REM in ways a merge cannot fix, and two of the .asset files carry the LAN IP of
REM whichever machine last ran the study.

cd /d "%~dp0"

REM -- --setup: teach git to merge Unity files with Unity's own tool --------
REM Run once on this machine. The rules are in .gitattributes and travel with
REM the repository; the driver they name is local git config and does not, so a
REM fresh clone has rules and no tool, and git quietly falls back to the
REM line-based merge those rules exist to prevent.
if "%~1"=="--setup" (
    set "YAMLMERGE="
    for /f "delims=" %%p in ('dir /b /s "C:\Program Files\Unity\Hub\Editor\UnityYAMLMerge.exe" 2^>nul') do set "YAMLMERGE=%%p"
    if not defined YAMLMERGE (
        echo [sync] UnityYAMLMerge.exe not found under C:\Program Files\Unity\Hub\Editor
        echo [sync] It ships with the editor, so Unity is probably installed elsewhere.
        exit /b 1
    )
    git config merge.unityyamlmerge.name "Unity SmartMerge"
    git config merge.unityyamlmerge.driver "'!YAMLMERGE!' merge -p \"$BASE\" \"$REMOTE\" \"$LOCAL\" \"$MERGED\""
    git config merge.unityyamlmerge.recursive binary
    echo [sync] Unity merge tool configured:
    echo [sync]   !YAMLMERGE!
    exit /b 0
)

for /f "usebackq tokens=*" %%b in (`git rev-parse --abbrev-ref HEAD`) do set BRANCH=%%b
if "%BRANCH%"=="HEAD" (
    echo [sync] Detached HEAD - checkout a branch first.
    exit /b 1
)
echo [sync] branch: %BRANCH%

REM -- Guard: participant data must never be staged -------------------------
git status --porcelain | findstr /r /c:"^?? Logs/" /c:"^[AM] Logs/" >nul 2>&1
if not errorlevel 1 (
    echo [sync] Logs/ is showing as trackable - participant data. Aborting.
    echo [sync] Check .gitignore still contains 'Logs/' before running this again.
    exit /b 1
)

REM -- 1. Put the per-machine files back ------------------------------------
REM Both carry this machine's LAN IP and must not travel to the other one.
git checkout -- "Unity/Assets/Demos/Server.asset" 2>nul
git checkout -- "Unity/Assets/Resources/DevAgentSettings.asset" 2>nul

REM -- 2. Pull --------------------------------------------------------------
echo [sync] fetching...
git fetch --quiet origin %BRANCH% 2>nul

git pull --rebase --autostash --quiet origin %BRANCH%
if errorlevel 1 (
    echo [sync] Pull hit a conflict, and this script will not guess its way out.
    echo [sync]   Fix the listed files, then:  git rebase --continue
    echo [sync]   Or abandon the pull with:    git rebase --abort
    exit /b 1
)
echo [sync] up to date with GitHub

if "%~1"=="--pull" (
    echo [sync] pull only, as asked. Done.
    exit /b 0
)

REM -- 3. Commit ------------------------------------------------------------
REM Tracked changes only. 'git add -A' swept up stale directories and a Unity
REM crash dump on its first real run, re-creating duplicates that had just been
REM removed. New files are listed below and added deliberately with git add.
git add -u
git reset --quiet -- "Unity/Assets/Demos/Server.asset" 2>nul
git reset --quiet -- "Unity/Assets/Resources/DevAgentSettings.asset" 2>nul

for /f "delims=" %%u in ('git ls-files --others --exclude-standard') do (
    echo [sync] not synced, untracked: %%u
)

git diff --cached --quiet
if errorlevel 1 (
    git diff --cached --name-only | findstr /r /c:"\.unity$" /c:"\.prefab$" >nul 2>&1
    if not errorlevel 1 (
        echo [sync] this commit touches a Unity scene or prefab - if the other
        echo [sync] machine edited the same one, expect a conflict needing Unity
    )

    if "%~1"=="" (
        for /f "usebackq tokens=*" %%d in (`powershell -NoProfile -Command "Get-Date -Format 'yyyy-MM-dd HH:mm'"`) do set STAMP=%%d
        git commit --quiet -m "Sync from %COMPUTERNAME% - !STAMP!"
    ) else (
        git commit --quiet -m "%~1"
    )
    echo [sync] committed
) else (
    echo [sync] nothing local to commit
)

REM -- 4. Push --------------------------------------------------------------
echo [sync] pushing...
git push --quiet origin %BRANCH%
if errorlevel 1 (
    echo [sync] Push rejected - the remote moved while this ran. Run sync again.
    exit /b 1
)
echo [sync] pushed. GitHub is up to date.
endlocal
