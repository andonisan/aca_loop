import { DefaultAzureCredential } from "@azure/identity";
import { QueueClient } from "@azure/storage-queue";

const queueClient = new QueueClient(
  `https://${process.env.STORAGE_ACCOUNT_NAME}.queue.core.windows.net/${process.env.QUEUE_NAME}`,
  new DefaultAzureCredential(),
);

// Dequeue at most one work item, hidden for the full replica timeout so a
// crashed execution's message reappears for retry (capped by the poison
// guard in the entrypoint).
export async function receiveWorkItem(visibilityTimeoutSeconds) {
  const response = await queueClient.receiveMessages({
    numberOfMessages: 1,
    visibilityTimeout: visibilityTimeoutSeconds,
  });

  const message = response.receivedMessageItems[0];
  if (!message) return null;

  return {
    id: message.messageId,
    popReceipt: message.popReceipt,
    dequeueCount: message.dequeueCount,
    workItem: JSON.parse(Buffer.from(message.messageText, "base64").toString("utf8")),
  };
}

export async function deleteMessage(message) {
  await queueClient.deleteMessage(message.id, message.popReceipt);
}
