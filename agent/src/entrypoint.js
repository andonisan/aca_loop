import { spawn } from "node:child_process";
import { mkdtempSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { receiveWorkItem, deleteMessage } from "./queue.js";
import { loadSecrets } from "./secrets.js";
import { getInstallationToken, getIssueLabels, setIssueBlocked, commentOnIssue } from "./github.js";

const MAX_DEQUEUE_COUNT = 3;
// The watchdog fires this long before the platform's replica timeout so the
// failure contract (status:blocked + comment) runs before the replica dies.
const WATCHDOG_BUFFER_SECONDS = 120;

const GATE_COMMANDS = {
  "status:ready-for-proposal": "/ds:propose",
  "status:ready-for-implementation": "/ds:implement",
};

const log = (...args) => console.log("[agent]", ...args);

function run(cmd, args, opts = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(cmd, args, { stdio: "inherit", ...opts });
    child.on("error", reject);
    child.on("close", (code) => (code === 0 ? resolve() : reject(new Error(`${cmd} exited ${code}`))));
  });
}

// Runs the agent CLI capturing combined output (for the WIP-refusal check
// and the failure-comment log tail) while still streaming it to our logs.
function runAgent(cmd, args, { env, cwd, timeoutMs }) {
  return new Promise((resolve) => {
    const chunks = [];
    let timedOut = false;

    const child = spawn(cmd, args, { cwd, env, stdio: ["ignore", "pipe", "pipe"] });

    const capture = (stream, sink) =>
      stream.on("data", (data) => {
        sink.write(data);
        chunks.push(data);
      });
    capture(child.stdout, process.stdout);
    capture(child.stderr, process.stderr);

    const watchdog = setTimeout(() => {
      timedOut = true;
      child.kill("SIGTERM");
      setTimeout(() => child.kill("SIGKILL"), 10_000).unref();
    }, timeoutMs);

    child.on("close", (code) => {
      clearTimeout(watchdog);
      resolve({ code, timedOut, output: Buffer.concat(chunks).toString("utf8") });
    });
    child.on("error", (err) => {
      clearTimeout(watchdog);
      resolve({ code: -1, timedOut, output: `failed to spawn ${cmd}: ${err.message}` });
    });
  });
}

function logTail(output, maxChars = 6000) {
  return output.length <= maxChars ? output : `…\n${output.slice(-maxChars)}`;
}

async function main() {
  const replicaTimeoutSeconds = Number(process.env.REPLICA_TIMEOUT_SECONDS);

  const message = await receiveWorkItem(replicaTimeoutSeconds);
  if (!message) {
    // Expected under KEDA overscale: more executions than queued work items.
    // Exit clean without ever contacting GitHub or an LLM provider.
    log("queue empty, nothing to do");
    return;
  }

  const { repo, issue_number: issueNumber, gate_label: gateLabel } = message.workItem;
  const runtime = message.workItem.agent_runtime || process.env.DEFAULT_AGENT_RUNTIME;

  if (message.dequeueCount > MAX_DEQUEUE_COUNT) {
    log(`poison message (dequeue_count=${message.dequeueCount}), dropping:`, JSON.stringify(message.workItem));
    await deleteMessage(message);
    return;
  }

  const command = GATE_COMMANDS[gateLabel];
  if (!repo || !issueNumber || !command) {
    log("malformed work item, dropping:", JSON.stringify(message.workItem));
    await deleteMessage(message);
    return;
  }

  log(`picked up ${repo}#${issueNumber} at gate ${gateLabel} (runtime=${runtime}, attempt ${message.dequeueCount})`);

  const secrets = await loadSecrets();
  const token = await getInstallationToken({
    appId: secrets.githubAppId,
    privateKey: secrets.githubAppPrivateKey,
    repoFullName: repo,
  });

  // Gate re-validation: the single-label invariant means the label moving is
  // the lock. A duplicate delivery, a human re-triaging, or another
  // execution having already started all look the same -- the gate label is
  // gone, so this execution has nothing to do.
  const labels = await getIssueLabels(token, repo, issueNumber);
  if (!labels.includes(gateLabel)) {
    log(`gate label ${gateLabel} no longer on ${repo}#${issueNumber}, exiting clean`);
    await deleteMessage(message);
    return;
  }

  const workdir = mkdtempSync(path.join(os.tmpdir(), "ai-loop-"));
  const repoDir = path.join(workdir, "repo");
  const botLogin = process.env.AGENT_BOT_LOGIN;

  const agentEnv = {
    ...process.env,
    GH_TOKEN: token,
    GITHUB_TOKEN: token,
    GIT_AUTHOR_NAME: botLogin,
    GIT_AUTHOR_EMAIL: `${secrets.githubAppId}+${botLogin}@users.noreply.github.com`,
    GIT_COMMITTER_NAME: botLogin,
    GIT_COMMITTER_EMAIL: `${secrets.githubAppId}+${botLogin}@users.noreply.github.com`,
    ...(secrets.anthropicApiKey ? { ANTHROPIC_API_KEY: secrets.anthropicApiKey } : {}),
    ...secrets.opencodeProviderEnv,
  };

  await run("git", ["clone", `https://x-access-token:${token}@github.com/${repo}.git`, repoDir], {
    env: agentEnv,
  });

  const prompt = `${command} #${issueNumber}`;
  const timeoutMs = Math.max(60, replicaTimeoutSeconds - WATCHDOG_BUFFER_SECONDS) * 1000;

  const invocation =
    runtime === "opencode"
      ? { cmd: "opencode", args: ["run", prompt] }
      : {
          cmd: "claude",
          args: [
            "-p",
            prompt,
            "--max-turns",
            process.env.AGENT_MAX_TURNS,
            "--allowed-tools",
            process.env.AGENT_ALLOWED_TOOLS,
          ],
        };

  log(`running: ${invocation.cmd} ${invocation.args.join(" ")}`);
  const result = await runAgent(invocation.cmd, invocation.args, {
    env: agentEnv,
    cwd: repoDir,
    timeoutMs,
  });

  if (result.code === 0 && !result.timedOut) {
    // Branching, PR creation, and the label flip all belong to the repo's
    // own /ds:* command -- success here just means the run completed.
    await deleteMessage(message);
    log(`${repo}#${issueNumber} completed`);
    return;
  }

  // WIP-limit refusal from /ds:implement is not a failure: keep the gate
  // label so a human (or a later re-label) can dispatch it again when
  // capacity frees up.
  const wipPattern = new RegExp(process.env.WIP_REFUSAL_PATTERN, "i");
  if (wipPattern.test(result.output)) {
    log(`${repo}#${issueNumber} refused by WIP limit, keeping gate label`);
    await commentOnIssue(
      token,
      repo,
      issueNumber,
      `🚦 The agent declined \`${command}\`: the WIP limit is reached. The \`${gateLabel}\` label was kept — remove and re-add it to retry once an in-progress issue completes.`,
    );
    await deleteMessage(message);
    return;
  }

  // Failure contract: agent runs are not idempotent-retryable, so instead of
  // requeueing we flip the issue to status:blocked (preserving the
  // single-label invariant) and leave the evidence in a comment.
  const reason = result.timedOut
    ? `timed out after ${Math.round(timeoutMs / 1000)}s`
    : `exited with code ${result.code}`;
  log(`${repo}#${issueNumber} failed: ${reason}`);

  try {
    await setIssueBlocked(token, repo, issueNumber);
    await commentOnIssue(
      token,
      repo,
      issueNumber,
      [
        `❌ Agent run for \`${command}\` (runtime: \`${runtime}\`) ${reason}.`,
        ``,
        `The issue was moved to \`status:blocked\`. Any partial branch or draft PR is left in place for inspection. Re-label to \`${gateLabel}\` to retry.`,
        ``,
        `<details><summary>Log tail</summary>`,
        ``,
        "```",
        logTail(result.output),
        "```",
        ``,
        `</details>`,
      ].join("\n"),
    );
  } finally {
    await deleteMessage(message);
  }
}

main().catch((err) => {
  // Unhandled failure before the trap could act: leave the message for the
  // visibility-timeout retry (capped by the poison guard).
  console.error("[agent] fatal:", err);
  process.exit(1);
});
