import crypto from "node:crypto";
import express from "express";
import { loadSecrets, getSecrets } from "./secrets.js";
import { enqueueWorkItem } from "./queue.js";

const app = express();

const gateLabels = new Set(process.env.GATE_LABELS.split(",").map((label) => label.trim()));

function timingSafeEqualStr(a, b) {
  const bufA = Buffer.from(a);
  const bufB = Buffer.from(b);
  if (bufA.length !== bufB.length) return false;
  return crypto.timingSafeEqual(bufA, bufB);
}

function verifyGithubSignature(rawBody, signatureHeader, secret) {
  if (!signatureHeader) return false;
  const expected = "sha256=" + crypto.createHmac("sha256", secret).update(rawBody).digest("hex");
  return timingSafeEqualStr(expected, signatureHeader);
}

app.get("/healthz", (req, res) => res.status(200).send("ok"));

app.post("/webhooks/github", express.raw({ type: "application/json", limit: "2mb" }), async (req, res) => {
  const { githubWebhookSecret } = getSecrets();

  if (!verifyGithubSignature(req.body, req.get("X-Hub-Signature-256"), githubWebhookSecret)) {
    return res.status(401).send("invalid signature");
  }

  // Ack fast -- GitHub's webhook delivery has a short timeout, and the
  // real work (enqueue) is cheap enough to do inline for this volume.
  res.status(202).send("accepted");

  if (req.get("X-GitHub-Event") !== "issues") return;

  let payload;
  try {
    payload = JSON.parse(req.body.toString("utf8"));
  } catch {
    return;
  }

  if (payload.action !== "labeled") return;

  // Only the machine-gate labels dispatch an agent. The agent's own label
  // writes (proposal-review, in-progress, code-review, blocked) are never
  // gate labels, so the loop can't re-trigger itself -- the sender check
  // below is belt-and-braces on top of that.
  const appliedLabel = payload.label?.name;
  if (!appliedLabel || !gateLabels.has(appliedLabel)) return;

  if (payload.sender?.login?.toLowerCase() === process.env.AGENT_BOT_LOGIN.toLowerCase()) return;

  try {
    await enqueueWorkItem({
      repo: payload.repository?.full_name,
      issue_number: payload.issue?.number,
      gate_label: appliedLabel,
      agent_runtime: process.env.DEFAULT_AGENT_RUNTIME,
      enqueued_at: new Date().toISOString(),
    });
  } catch (err) {
    console.error("failed to enqueue work item", err);
  }
});

async function main() {
  const backoffsMs = [1000, 2000, 5000, 10000, 30000];
  for (let attempt = 0; ; attempt++) {
    try {
      await loadSecrets();
      break;
    } catch (err) {
      const delay = backoffsMs[Math.min(attempt, backoffsMs.length - 1)];
      console.error(`failed to load secrets (attempt ${attempt + 1}), retrying in ${delay}ms`, err);
      await new Promise((resolve) => setTimeout(resolve, delay));
    }
  }

  const port = process.env.PORT ?? 3000;
  app.listen(port, () => console.log(`ai-loop webhook receiver listening on ${port}`));
}

main();
