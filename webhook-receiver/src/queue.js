import { DefaultAzureCredential } from "@azure/identity";
import { QueueClient } from "@azure/storage-queue";

const queueClient = new QueueClient(
  `https://${process.env.STORAGE_ACCOUNT_NAME}.queue.core.windows.net/${process.env.QUEUE_NAME}`,
  new DefaultAzureCredential(),
);

export async function enqueueWorkItem(workItem) {
  // base64-encode so the agent entrypoint's default decoding matches what
  // was sent.
  const body = Buffer.from(JSON.stringify(workItem)).toString("base64");
  await queueClient.sendMessage(body);
}
