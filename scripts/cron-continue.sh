#!/usr/bin/env bash
# Local cron backstop for the Forge autonomous build loop.
# Runs Claude Code headlessly against this repo so work continues even if
# the interactive /loop session (ScheduleWakeup) isn't running anymore.
set -uo pipefail

export PATH="/home/felippe/.local/bin:/home/felippe/.npm-global/bin:/home/felippe/.dotnet:/home/felippe/.dotnet/tools:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"

REPO_DIR="/home/felippe/repos/Forge"
LOG_FILE="$HOME/.claude/forge-cron.log"
LOCK_FILE="/tmp/forge-cron.lock"

exec 9>"$LOCK_FILE"
if ! flock -n 9; then
  echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) skipped: previous run still in progress" >> "$LOG_FILE"
  exit 0
fi

cd "$REPO_DIR" || exit 1

{
  echo "=== $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="
  claude -p "Verifique o estado atual do projeto Forge neste repositorio (git log, docs/, docs/adr/) e continue construindo de forma autonoma, seguindo docs/000-Vision.md e os ADRs 0001-0004, avancando fase por fase (dominio -> arquitetura -> backend/frontend/worker skeleton), commitando e dando push a cada fase concluida. So pare e avise (deixe isso registrado claramente na saida) em caso de acao destrutiva, pre-requisito externo faltando, ou ambiguidade real nao coberta pelos docs/ADRs. Se nao houver nenhum progresso possivel no momento (por exemplo, esperando um pre-requisito externo ja sinalizado), nao repita trabalho ja feito - apenas confirme o estado e finalize." \
    --output-format text
  echo "=== end $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="
} >> "$LOG_FILE" 2>&1
