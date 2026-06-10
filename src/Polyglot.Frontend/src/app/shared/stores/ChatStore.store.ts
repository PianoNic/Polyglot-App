import { computed, inject } from '@angular/core';
import {
  HttpErrorResponse,
  HttpEventType,
  type HttpDownloadProgressEvent,
  type HttpEvent,
  type HttpResponse,
} from '@angular/common/http';
import { firstValueFrom, type Observable } from 'rxjs';
import {
  patchState,
  signalStore,
  withComputed,
  withHooks,
  withMethods,
  withState,
} from '@ngrx/signals';
import { AttachmentService } from '../../api/api/attachment.service';
import { ChatService } from '../../api/api/chat.service';
import { ModelService } from '../../api/api/model.service';
import type { AttachmentDto } from '../../api/model/attachmentDto';
import type { AvailableModelDto } from '../../api/model/availableModelDto';
import type { ChatStreamPayload } from '../../api/model/chatStreamPayload';
import { ChatStreamPayloadType } from '../../api/model/chatStreamPayloadType';
import type { MessageDto } from '../../api/model/messageDto';
import type { SendMessageDto } from '../../api/model/sendMessageDto';
import { MessageRole } from '../../api/model/messageRole';
import type { Conversation } from '../../../../libs/prompt-kit/conversation-list/pk-conversation-types';

const SELECTED_MODEL_KEY = 'polyglot.selectedModel';

export type SendResult =
  | { kind: 'sent'; newId: string | null }
  | { kind: 'error'; error: string };

type ChatStoreState = {
  chats: Conversation[];
  activeChatId: string | null;
  activeChatTitle: string | null;
  messages: MessageDto[];
  models: AvailableModelDto[];
  selectedModelId: string | null;
  isSending: boolean;
  streamingText: string;
  streamDone: boolean;
  pendingAttachments: AttachmentDto[];
  isLoadingChat: boolean;
  sendError: string | null;
  chatsLoaded: boolean;
  modelsLoaded: boolean;
};

export const initialChatStore: ChatStoreState = {
  chats: [],
  activeChatId: null,
  activeChatTitle: null,
  messages: [],
  models: [],
  selectedModelId: readPersistedModel(),
  isSending: false,
  streamingText: '',
  streamDone: false,
  pendingAttachments: [],
  isLoadingChat: false,
  sendError: null,
  chatsLoaded: false,
  modelsLoaded: false,
};

export const ChatStore = signalStore(
  { providedIn: 'root' },
  withState(initialChatStore),
  withComputed((store) => ({
    activeModel: computed<AvailableModelDto | null>(() => {
      const id = store.selectedModelId();
      return id ? (store.models().find((m) => m.id === id) ?? null) : null;
    }),
    tokenSums: computed(() => {
      let input = 0;
      let output = 0;
      for (const m of store.messages()) {
        const t = Math.ceil(m.content.length / 4);
        if (m.role === MessageRole.User) input += t;
        else if (m.role === MessageRole.Assistant) output += t;
      }
      return { input, output };
    }),
  })),
  withComputed((store) => ({
    estimatedInputTokens: computed(() => store.tokenSums().input),
    estimatedOutputTokens: computed(() => store.tokenSums().output),
  })),
  withMethods((store) => {
    const chatApi = inject(ChatService);
    const modelApi = inject(ModelService);
    const attachmentApi = inject(AttachmentService);
    let inFlight: { id: string; promise: Promise<void> } | null = null;
    let pendingStream: { response: SendMessageDto; optimisticId: string } | null = null;

    function touchChat(id: string | null): void {
      if (!id)
        return;
      const list = store.chats();
      if (!list.some((c) => c.id === id))
        return;
      const now = new Date();
      patchState(store, {
        chats: list
          .map((c) => (c.id === id ? { ...c, updatedAt: now } : c))
          .sort(byUpdatedDesc),
      });
    }

    async function loadChats(force = false): Promise<void> {
      if (store.chatsLoaded() && !force)
        return;
      patchState(store, { chatsLoaded: true });
      try {
        const list = await firstValueFrom(chatApi.apiChatGet());
        patchState(store, { chats: list });
      } catch (err) {
        patchState(store, { chatsLoaded: false });
        throw err;
      }
    }

    function setSelectedModel(id: string): void {
      patchState(store, { selectedModelId: id });
      if (typeof localStorage !== 'undefined') {
        localStorage.setItem(SELECTED_MODEL_KEY, id);
      }
    }

    async function loadModels(): Promise<void> {
      if (store.modelsLoaded())
        return;
      patchState(store, { modelsLoaded: true });
      try {
        const list = await firstValueFrom(modelApi.apiModelListGet());
        patchState(store, { models: list });
        if (!store.selectedModelId() && list.length > 0) {
          setSelectedModel(list[0].id);
        }
      } catch (err) {
        patchState(store, { modelsLoaded: false });
        throw err;
      }
    }

    function openChat(id: string): Promise<void> {
      if (inFlight?.id === id)
        return inFlight.promise;
      if (store.activeChatId() === id && !inFlight)
        return Promise.resolve();

      patchState(store, { activeChatId: id, activeChatTitle: null, messages: [], isLoadingChat: true });
      const promise = (async () => {
        try {
          const detail = await firstValueFrom(chatApi.apiChatIdGet(id));
          if (inFlight?.id !== id)
            return;
          patchState(store, { messages: detail.messages, activeChatTitle: detail.title });
        } finally {
          if (inFlight?.id === id) {
            patchState(store, { isLoadingChat: false });
            inFlight = null;
          }
        }
      })();
      inFlight = { id, promise };
      return promise;
    }

    function newChat(): void {
      if (store.activeChatId() === null && store.messages().length === 0 && !inFlight)
        return;
      patchState(store, { activeChatId: null, activeChatTitle: null, messages: [] });
      inFlight = null;
    }

    async function sendMessage(text: string): Promise<SendResult> {
      const trimmed = text.trim();
      const model = store.selectedModelId();
      if (!trimmed || !model)
        return { kind: 'error', error: 'Pick a model and type a message.' };
      if (store.isSending() || store.streamingText())
        return { kind: 'error', error: 'A message is already being sent.' };

      const attachments = store.pendingAttachments();
      const optimistic: MessageDto = {
        id: `temp-${crypto.randomUUID()}`,
        role: MessageRole.User,
        content: trimmed,
        sequenceNumber: store.messages().length,
        createdAt: new Date().toISOString(),
        attachments,
      };
      patchState(store, (state) => ({
        isSending: true,
        sendError: null,
        streamingText: '',
        streamDone: false,
        pendingAttachments: [],
        messages: [...state.messages, optimistic],
      }));

      try {
        const response = await streamSend(
          chatApi.apiChatPost(
            {
              chatId: store.activeChatId(),
              message: trimmed,
              model,
              attachmentIds: attachments.map((a) => a.id),
            },
            'events',
            true,
          ),
          (token) => patchState(store, (state) => ({ streamingText: state.streamingText + token })),
        );

        // Hold the final messages until the reveal animation catches up —
        // commitStream() (wired to pk-response-stream's `finished` output)
        // performs the actual swap.
        pendingStream = { response, optimisticId: optimistic.id };
        patchState(store, { activeChatTitle: response.chatTitle, isSending: false, streamDone: true });

        if (!store.streamingText())
          commitStream();

        if (response.chatId !== store.activeChatId()) {
          patchState(store, { activeChatId: response.chatId });
          await loadChats(true);
          return { kind: 'sent', newId: response.chatId };
        }
        touchChat(store.activeChatId());
        return { kind: 'sent', newId: null };
      } catch (err) {
        const message = describeError(err);
        pendingStream = null;
        patchState(store, {
          messages: store.messages().filter((m) => m.id !== optimistic.id),
          sendError: message,
          isSending: false,
          streamingText: '',
          streamDone: false,
          pendingAttachments: attachments,
        });
        return { kind: 'error', error: message };
      }
    }

    function commitStream(): void {
      if (!pendingStream)
        return;
      const { response, optimisticId } = pendingStream;
      pendingStream = null;
      patchState(store, {
        messages: [
          ...store.messages().filter((m) => m.id !== optimisticId),
          response.userMessage,
          response.assistantMessage,
        ],
        streamingText: '',
        streamDone: false,
      });
    }

    function clearSendError(): void {
      patchState(store, { sendError: null });
    }

    async function uploadAttachment(file: File): Promise<void> {
      try {
        const dto = await firstValueFrom(attachmentApi.apiAttachmentPost(file));
        patchState(store, (state) => ({ pendingAttachments: [...state.pendingAttachments, dto] }));
      } catch (err) {
        patchState(store, { sendError: describeError(err) });
      }
    }

    function removeAttachment(id: string): void {
      patchState(store, (state) => ({
        pendingAttachments: state.pendingAttachments.filter((a) => a.id !== id),
      }));
    }

    async function renameChat(id: string, title: string): Promise<void> {
      await firstValueFrom(chatApi.apiChatIdPut(id, { title }));
      const list = store.chats();
      if (!list.some((c) => c.id === id))
        return;
      const now = new Date();
      patchState(store, {
        chats: list.map((c) => (c.id === id ? { ...c, title, updatedAt: now } : c)),
        ...(store.activeChatId() === id ? { activeChatTitle: title } : {}),
      });
    }

    async function deleteChat(id: string): Promise<void> {
      await firstValueFrom(chatApi.apiChatIdDelete(id));
      patchState(store, { chats: store.chats().filter((c) => c.id !== id) });
      if (inFlight?.id === id)
        inFlight = null;
      if (store.activeChatId() === id)
        newChat();
    }

    return {
      loadChats,
      loadModels,
      openChat,
      newChat,
      setSelectedModel,
      sendMessage,
      commitStream,
      clearSendError,
      uploadAttachment,
      removeAttachment,
      renameChat,
      deleteChat,
    };
  }),
  withHooks(() => ({})),
);

function byUpdatedDesc(a: Conversation, b: Conversation): number {
  return new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime();
}

/**
 * Consumes the SSE stream from POST /api/Chat. The generated client requests
 * `text/event-stream` with `responseType: 'text'`, so progress events carry the
 * cumulative raw body; complete frames are parsed out as they arrive.
 */
function streamSend(
  events$: Observable<HttpEvent<ChatStreamPayload>>,
  onToken: (token: string) => void,
): Promise<SendMessageDto> {
  return new Promise((resolve, reject) => {
    let parsedUpTo = 0;
    let buffer = '';
    let result: SendMessageDto | null = null;
    let failed: string | null = null;

    function ingest(cumulativeText: string): void {
      buffer += cumulativeText.slice(parsedUpTo).replace(/\r\n/g, '\n');
      parsedUpTo = cumulativeText.length;
      buffer = consumeSseFrames(buffer, (data) => {
        // The discriminator lives in the payload (not the SSE event name) so
        // the same handling works over any transport, e.g. WebSockets.
        const payload = JSON.parse(data) as ChatStreamPayload;
        if (payload.type === ChatStreamPayloadType.Chunk && payload.text) {
          onToken(payload.text);
        } else if (payload.type === ChatStreamPayloadType.Done && payload.result) {
          result = payload.result;
        } else if (payload.type === ChatStreamPayloadType.Error) {
          failed = payload.error ?? 'Send failed.';
        }
      });
    }

    events$.subscribe({
      next: (event) => {
        if (event.type === HttpEventType.DownloadProgress) {
          ingest((event as HttpDownloadProgressEvent).partialText ?? '');
        } else if (event.type === HttpEventType.Response) {
          ingest(((event as HttpResponse<unknown>).body as string | null) ?? '');
          if (failed)
            reject(new Error(failed));
          else if (result)
            resolve(result);
          else
            reject(new Error('The stream ended unexpectedly.'));
        }
      },
      error: (err) => reject(err),
    });
  });
}

function consumeSseFrames(
  buffer: string,
  handle: (data: string) => void,
): string {
  let separatorIndex = buffer.indexOf('\n\n');
  while (separatorIndex !== -1) {
    const rawFrame = buffer.slice(0, separatorIndex);
    buffer = buffer.slice(separatorIndex + 2);
    const dataLines: string[] = [];
    for (const line of rawFrame.split('\n')) {
      if (line.startsWith('data:'))
        dataLines.push(line.slice(line.startsWith('data: ') ? 'data: '.length : 'data:'.length));
    }
    if (dataLines.length > 0)
      handle(dataLines.join('\n'));
    separatorIndex = buffer.indexOf('\n\n');
  }
  return buffer;
}

function describeError(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    if (err.status === 0)
      return 'Network error. Check your connection and try again.';
    if (err.status === 401)
      return 'Your session expired. Sign in again to continue.';
    if (err.status === 402 || err.status === 403)
      return 'Out of credits or not authorized for this model.';
    if (err.status === 429)
      return 'Rate limited. Please wait a moment and retry.';
    if (err.status >= 500)
      return 'The server hit an error. Try again in a moment.';
    const detail = (err.error as { detail?: string; title?: string } | null) ?? null;
    return detail?.detail ?? detail?.title ?? err.message ?? 'Request failed.';
  }
  return err instanceof Error ? err.message : 'Something went wrong.';
}

function readPersistedModel(): string | null {
  if (typeof localStorage === 'undefined')
    return null;
  return localStorage.getItem(SELECTED_MODEL_KEY);
}
