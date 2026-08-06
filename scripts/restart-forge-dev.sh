#!/usr/bin/env bash
# docs/015-Deployment.md §2 - the "Forge" project's own restartTargets command
# (self-hosting: Forge reviewing changes to its own codebase). Deliberately restarts
# ONLY Forge.Api and the Vite frontend, never Forge.Worker.
#
# Why not Forge.Worker: this script runs as part of AgentActivities.DeployAsync,
# which executes *inside* the Forge.Worker process (it's the Temporal Worker running
# the activity). If this script killed Forge.Worker, it would kill the very process
# currently running it, mid-activity - the activity could never report success, and
# the health check afterward would have nothing left to poll it with. A task that
# changes Forge.Workflows/Forge.Worker code needs a manual Worker restart after review
# (the same restart this whole session has been doing by hand) - a real limitation of
# self-hosting, not hidden here.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOG_DIR="/tmp/forge-dev-logs"
mkdir -p "$LOG_DIR"

echo "[restart-forge-dev] stopping Forge.Api + frontend..."
# Matched on the unique --urls argument / vite's own port flag, not a bare "dotnet
# run" - Forge.Worker's own process has neither and must survive this script.
pkill -f "dotnet run --urls http://localhost:5080" 2>/dev/null || true
pkill -f "vite --port 5173" 2>/dev/null || true
sleep 1

echo "[restart-forge-dev] rebuilding backend..."
(cd "$REPO_ROOT/backend" && dotnet build > "$LOG_DIR/build.log" 2>&1)

echo "[restart-forge-dev] starting Forge.Api..."
# Found live (2026-08-06): `(cd X && setsid nohup CMD &)` leaves the subshell itself
# stuck in do_wait() on its backgrounded job instead of exiting - it never released
# the stdout/stderr pipe RunShellAsync (AgentActivities.cs) reads via ReadToEndAsync,
# so the whole Deploy activity hung forever even though CMD itself started fine and
# kept running correctly. `exec` replaces the subshell's own process image with
# setsid/nohup/dotnet directly - no bash left behind to wait on anything - and
# `disown` drops it from this script's job table so backgrounding it doesn't block
# the script's own exit either. Belt and suspenders, but this bug wedged every
# subsequent Deploy for the project (via the new per-project semaphore) until the
# Worker was restarted, so both layers are worth keeping.
(cd "$REPO_ROOT/backend/src/Forge.Api" && \
  exec setsid nohup dotnet run --urls http://localhost:5080 \
    > "$LOG_DIR/api.log" 2>&1 < /dev/null) &
disown

echo "[restart-forge-dev] starting frontend..."
(cd "$REPO_ROOT/frontend" && \
  exec setsid nohup npx vite --port 5173 --host \
    > "$LOG_DIR/frontend.log" 2>&1 < /dev/null) &
disown

echo "[restart-forge-dev] issued - logs in $LOG_DIR"
