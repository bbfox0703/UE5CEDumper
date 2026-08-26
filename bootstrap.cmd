@echo off
:: Toolchain bootstrap launcher. Two lines of dispatch on purpose -- the logic lives in
:: tools\bootstrap.py, because an unsigned create-heavy .ps1 is the exact shape Bitdefender
:: ATD quarantined six files over on this machine (docs/working-lessons.md §3.8). A .cmd
:: that only forwards arguments is not that shape.
::
:: Reasoning for every tool: docs\toolchain.md
::
::   bootstrap.cmd                     detect and report, install NOTHING (default)
::   bootstrap.cmd --dry-run           print the exact commands it would run
::   bootstrap.cmd --install           install the missing pieces (some need an elevated shell)
::   bootstrap.cmd --verify            ...and run the 13 gates afterwards
::   bootstrap.cmd --tiers build       only what is needed to compile
::   bootstrap.cmd --tiers build,gates,re,live,contrib      everything
chcp 65001 >nul
where py >nul 2>&1 || (
  echo(
  echo   Python's "py" launcher was not found.
  echo   Install it first:  winget install --id Python.Python.3.12 --exact
  echo                      winget install --id Python.Launcher    --exact
  echo   Then re-run this script. See docs\toolchain.md section 3.
  echo(
  exit /b 10
)
py "%~dp0tools\bootstrap.py" %*
