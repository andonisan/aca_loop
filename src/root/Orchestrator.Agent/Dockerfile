# Build context is `src/` (the .NET solution root) -- this project references sibling shared/
# and modules/ projects, so a narrower context can't resolve them.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish root/Orchestrator.Agent/Orchestrator.Agent.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final

# git + curl for the clone and general shell work; gh so the target repo's own /ds:* commands can
# drive GitHub (auth via GH_TOKEN, no login); Node.js because Claude Code and opencode are npm
# packages -- both agent runtimes read the same command files from the target repo
# (.claude/commands / .opencode/commands), so which one runs is a per-message choice, not an image
# variant (ported from the v1 Node image, agent/Dockerfile).
RUN apt-get update \
  && apt-get install -y --no-install-recommends git curl ca-certificates jq gnupg \
  && curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg \
    -o /usr/share/keyrings/githubcli-archive-keyring.gpg \
  && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" \
    > /etc/apt/sources.list.d/github-cli.list \
  && curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
  && apt-get update \
  && apt-get install -y --no-install-recommends gh nodejs \
  && npm install -g @anthropic-ai/claude-code opencode-ai \
  && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# Tunable without a rebuild via Container Apps env overrides (double-underscore is .NET
# configuration's env-var section separator: Execution__AllowedTools -> Execution:AllowedTools).
ENV Execution__AllowedTools="Bash,Edit,Write,Read,Glob,Grep,WebFetch,WebSearch,TodoWrite,Task"
ENV Execution__WipRefusalPattern="WIP limit"

USER $APP_UID
ENTRYPOINT ["dotnet", "Orchestrator.Agent.dll"]
