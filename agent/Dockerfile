FROM node:22-bookworm-slim

# git + curl for the entrypoint's clone and the agent's own shell work;
# gh so the /ds:* commands can drive GitHub (auth via GH_TOKEN, no login).
RUN apt-get update \
  && apt-get install -y --no-install-recommends git curl ca-certificates jq \
  && curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg \
    -o /usr/share/keyrings/githubcli-archive-keyring.gpg \
  && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" \
    > /etc/apt/sources.list.d/github-cli.list \
  && apt-get update \
  && apt-get install -y --no-install-recommends gh \
  && rm -rf /var/lib/apt/lists/*

# Both agent runtimes. They read the same command files from the target
# repo (.claude/commands / .opencode/commands), so which one runs is a
# per-work-item choice, not an image variant.
RUN npm install -g @anthropic-ai/claude-code opencode-ai

WORKDIR /app

COPY package.json package-lock.json* ./
RUN npm install --omit=dev

COPY src/ ./src/

ENV NODE_ENV=production
# Tunable without a rebuild via Container Apps env overrides.
ENV AGENT_ALLOWED_TOOLS="Bash,Edit,Write,Read,Glob,Grep,WebFetch,WebSearch,TodoWrite,Task"
ENV WIP_REFUSAL_PATTERN="WIP limit"

USER node
ENV HOME=/home/node

CMD ["node", "src/entrypoint.js"]
