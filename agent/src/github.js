import jwt from "jsonwebtoken";

const GITHUB_API = "https://api.github.com";

function buildAppJwt(appId, privateKey) {
  const now = Math.floor(Date.now() / 1000);
  return jwt.sign({ iat: now - 60, exp: now + 540, iss: appId }, privateKey, {
    algorithm: "RS256",
  });
}

// Mints an installation token scoped to the single target repository --
// short-lived (<=1h, GitHub-enforced) and never persisted.
export async function getInstallationToken({ appId, privateKey, repoFullName }) {
  const installationId = process.env.GITHUB_APP_INSTALLATION_ID;
  const repoName = repoFullName.split("/")[1];

  const res = await fetch(`${GITHUB_API}/app/installations/${installationId}/access_tokens`, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${buildAppJwt(appId, privateKey)}`,
      Accept: "application/vnd.github+json",
      "X-GitHub-Api-Version": "2022-11-28",
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ repositories: [repoName] }),
  });
  if (!res.ok) {
    throw new Error(`installation token exchange failed: ${res.status} ${await res.text()}`);
  }
  const body = await res.json();
  return body.token;
}

async function githubRequest(token, method, path, body) {
  const res = await fetch(`${GITHUB_API}${path}`, {
    method,
    headers: {
      Authorization: `Bearer ${token}`,
      Accept: "application/vnd.github+json",
      "X-GitHub-Api-Version": "2022-11-28",
      ...(body ? { "Content-Type": "application/json" } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!res.ok) {
    throw new Error(`${method} ${path} failed: ${res.status} ${await res.text()}`);
  }
  return res.json();
}

export async function getIssueLabels(token, repoFullName, issueNumber) {
  const issue = await githubRequest(token, "GET", `/repos/${repoFullName}/issues/${issueNumber}`);
  return issue.labels.map((label) => label.name);
}

// Replaces the issue's status:* label with status:blocked in a single call,
// preserving both the non-status labels and the single-label invariant.
export async function setIssueBlocked(token, repoFullName, issueNumber) {
  const labels = await getIssueLabels(token, repoFullName, issueNumber);
  const newLabels = labels.filter((name) => !name.startsWith("status:"));
  newLabels.push("status:blocked");
  await githubRequest(token, "PUT", `/repos/${repoFullName}/issues/${issueNumber}/labels`, {
    labels: newLabels,
  });
}

export async function commentOnIssue(token, repoFullName, issueNumber, body) {
  await githubRequest(token, "POST", `/repos/${repoFullName}/issues/${issueNumber}/comments`, {
    body,
  });
}
