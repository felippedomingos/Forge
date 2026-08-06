# Build context is backend/ (see docker-compose.yml at repo root).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Forge.Worker/Forge.Worker.csproj src/Forge.Worker/
COPY src/Forge.Domain/Forge.Domain.csproj src/Forge.Domain/
COPY src/Forge.Infrastructure/Forge.Infrastructure.csproj src/Forge.Infrastructure/
COPY src/Forge.Workflows/Forge.Workflows.csproj src/Forge.Workflows/
RUN dotnet restore src/Forge.Worker/Forge.Worker.csproj

COPY src/Forge.Worker/ src/Forge.Worker/
COPY src/Forge.Domain/ src/Forge.Domain/
COPY src/Forge.Infrastructure/ src/Forge.Infrastructure/
COPY src/Forge.Workflows/ src/Forge.Workflows/
RUN dotnet publish src/Forge.Worker/Forge.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# GitOps.cs / ClaudeCliProvider.cs (Forge.Workflows) shell out to git/gh/az/claude via
# bare Process.Start, resolved from PATH - docs/005-Agents.md §6, docs/adr/ADR-0005.
# Previously assumed present on whatever host ran the Worker; installed here instead so
# the image is self-contained.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        git curl ca-certificates gnupg lsb-release apt-transport-https wget \
    # GitHub CLI (gh) - official apt repo per cli.github.com/packages
    && mkdir -p -m 755 /etc/apt/keyrings \
    && wget -qO- https://cli.github.com/packages/githubcli-archive-keyring.gpg | tee /etc/apt/keyrings/githubcli-archive-keyring.gpg > /dev/null \
    && chmod go+r /etc/apt/keyrings/githubcli-archive-keyring.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" > /etc/apt/sources.list.d/github-cli.list \
    # Azure CLI (az) - Microsoft's official Debian/Ubuntu install script
    && curl -sL https://aka.ms/InstallAzureCLIDeb | bash \
    # Node.js - only needed to install the Claude Code CLI npm package below
    && curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get update \
    && apt-get install -y --no-install-recommends gh nodejs \
    && npm install -g @anthropic-ai/claude-code \
    # GitOps.RunAzAsync calls `az repos pr create`/`pr show` - part of the azure-devops
    # extension, not az core, so it has to be added explicitly.
    && az extension add --name azure-devops --yes \
    && apt-get purge -y --auto-remove wget lsb-release apt-transport-https gnupg \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# AgentActivities reads TEMPORAL_ADDRESS / FORGE_CONNECTION_STRING / FORGE_WORKTREES_DIR;
# gh/az/claude pick up GH_TOKEN, AZURE_DEVOPS_EXT_PAT, CLAUDE_CONFIG_DIR respectively -
# all supplied via docker-compose.yml / .env, none baked into this image.
ENTRYPOINT ["dotnet", "Forge.Worker.dll"]
