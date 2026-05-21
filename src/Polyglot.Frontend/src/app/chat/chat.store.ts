import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ChatService } from '../api/api/chat.service';
import { ModelService } from '../api/api/model.service';
import type { AvailableModelDto } from '../api/model/availableModelDto';
import type { ChatDto } from '../api/model/chatDto';
import type { MessageDto } from '../api/model/messageDto';
import { MessageRole } from '../api/model/messageRole';
import type { Model } from '../../../libs/prompt-kit/model-picker/pk-model-types';
import type { Conversation } from '../../../libs/prompt-kit/conversation-list/pk-conversation-types';

const SELECTED_MODEL_KEY = 'polyglot.selectedModel';

export type SendResult =
  | { kind: 'sent'; newId: string | null }
  | { kind: 'error'; error: string };

@Injectable({ providedIn: 'root' })
export class ChatStore {
  private readonly _chatApi = inject(ChatService);
  private readonly _modelApi = inject(ModelService);

  readonly chats = signal<readonly ChatDto[]>([]);
  readonly activeChatId = signal<string | null>(null);
  readonly messages = signal<readonly MessageDto[]>([]);
  readonly models = signal<readonly AvailableModelDto[]>([]);
  readonly selectedModelId = signal<string | null>(this.readPersistedModel());
  readonly isSending = signal(false);
  readonly isLoadingChat = signal(false);
  readonly sendError = signal<string | null>(null);

  private _chatsLoaded = false;
  private _modelsLoaded = false;
  private _inFlight: { id: string; promise: Promise<void> } | null = null;

  readonly conversations = computed<Conversation[]>(() =>
    this.chats().map((c) => ({
      id: c.id ?? '',
      title: c.title?.trim() ? c.title : 'Untitled chat',
      updatedAt: c.updatedAt ?? c.createdAt ?? new Date().toISOString(),
    })),
  );

  readonly modelOptions = computed<Model[]>(() =>
    this.models()
      .filter((m): m is AvailableModelDto & { id: string } => !!m.id)
      .map((m) => ({
        id: m.id,
        name: m.name ?? m.id,
        provider: providerFromId(m.id),
        inputPricePer1M: m.promptPricePerMillion,
        outputPricePer1M: m.completionPricePerMillion,
        currency: 'USD',
      })),
  );

  readonly activeModel = computed<AvailableModelDto | null>(() => {
    const id = this.selectedModelId();
    return id ? (this.models().find((m) => m.id === id) ?? null) : null;
  });

  readonly activeChatTitle = computed(() => {
    const id = this.activeChatId();
    if (!id) return 'New chat';
    return this.chats().find((c) => c.id === id)?.title ?? 'New chat';
  });

  private readonly _tokenSums = computed(() => {
    let input = 0;
    let output = 0;
    for (const m of this.messages()) {
      const t = Math.ceil((m.content?.length ?? 0) / 4);
      if (m.role === MessageRole.User) input += t;
      else if (m.role === MessageRole.Assistant) output += t;
    }
    return { input, output };
  });
  readonly estimatedInputTokens = computed(() => this._tokenSums().input);
  readonly estimatedOutputTokens = computed(() => this._tokenSums().output);

  async loadChats(force = false): Promise<void> {
    if (this._chatsLoaded && !force) return;
    this._chatsLoaded = true;
    try {
      const list = await firstValueFrom(this._chatApi.apiChatGet());
      this.chats.set([...list].sort(byUpdatedDesc));
    } catch (err) {
      this._chatsLoaded = false;
      throw err;
    }
  }

  async loadModels(): Promise<void> {
    if (this._modelsLoaded) return;
    this._modelsLoaded = true;
    try {
      const list = await firstValueFrom(this._modelApi.apiModelListGet());
      this.models.set(list);
      if (!this.selectedModelId() && list.length > 0 && list[0].id) {
        this.setSelectedModel(list[0].id);
      }
    } catch (err) {
      this._modelsLoaded = false;
      throw err;
    }
  }

  openChat(id: string): Promise<void> {
    if (this._inFlight?.id === id) return this._inFlight.promise;
    if (this.activeChatId() === id && !this._inFlight) return Promise.resolve();

    this.activeChatId.set(id);
    this.messages.set([]);
    this.isLoadingChat.set(true);
    const promise = (async () => {
      try {
        const detail = await firstValueFrom(this._chatApi.apiChatIdGet(id));
        if (this._inFlight?.id !== id) return;
        this.messages.set(detail.messages ?? []);
      } finally {
        if (this._inFlight?.id === id) {
          this.isLoadingChat.set(false);
          this._inFlight = null;
        }
      }
    })();
    this._inFlight = { id, promise };
    return promise;
  }

  newChat(): void {
    if (this.activeChatId() === null && this.messages().length === 0 && !this._inFlight) return;
    this.activeChatId.set(null);
    this.messages.set([]);
    this._inFlight = null;
  }

  setSelectedModel(id: string): void {
    this.selectedModelId.set(id);
    if (typeof localStorage !== 'undefined') {
      localStorage.setItem(SELECTED_MODEL_KEY, id);
    }
  }

  async sendMessage(text: string): Promise<SendResult> {
    const trimmed = text.trim();
    const model = this.selectedModelId();
    if (!trimmed || !model) return { kind: 'error', error: 'Pick a model and type a message.' };
    if (this.isSending()) return { kind: 'error', error: 'A message is already being sent.' };

    this.isSending.set(true);
    this.sendError.set(null);
    try {
      const response = await firstValueFrom(
        this._chatApi.apiChatPost({
          chatId: this.activeChatId(),
          message: trimmed,
          model,
        }),
      );

      const next = [...this.messages()];
      if (response.userMessage) next.push(response.userMessage);
      if (response.assistantMessage) next.push(response.assistantMessage);
      this.messages.set(next);

      const newId = response.chatId ?? null;
      if (newId && newId !== this.activeChatId()) {
        this.activeChatId.set(newId);
        await this.loadChats(true);
        return { kind: 'sent', newId };
      }
      this.touchChat(this.activeChatId());
      return { kind: 'sent', newId: null };
    } catch (err) {
      const message = describeError(err);
      this.sendError.set(message);
      return { kind: 'error', error: message };
    } finally {
      this.isSending.set(false);
    }
  }

  clearSendError(): void {
    this.sendError.set(null);
  }

  async renameChat(id: string, title: string): Promise<void> {
    await firstValueFrom(this._chatApi.apiChatIdPut(id, { title }));
    const list = this.chats();
    if (!list.some((c) => c.id === id)) return;
    const now = new Date().toISOString();
    this.chats.set(list.map((c) => (c.id === id ? { ...c, title, updatedAt: now } : c)));
  }

  async deleteChat(id: string): Promise<void> {
    await firstValueFrom(this._chatApi.apiChatIdDelete(id));
    this.chats.update((list) => list.filter((c) => c.id !== id));
    if (this._inFlight?.id === id) this._inFlight = null;
    if (this.activeChatId() === id) this.newChat();
  }

  private touchChat(id: string | null): void {
    if (!id) return;
    const list = this.chats();
    if (!list.some((c) => c.id === id)) return;
    const now = new Date().toISOString();
    this.chats.set(
      list.map((c) => (c.id === id ? { ...c, updatedAt: now } : c)).sort(byUpdatedDesc),
    );
  }

  private readPersistedModel(): string | null {
    if (typeof localStorage === 'undefined') return null;
    return localStorage.getItem(SELECTED_MODEL_KEY);
  }
}

function byUpdatedDesc(a: ChatDto, b: ChatDto): number {
  const ta = new Date(a.updatedAt ?? a.createdAt ?? 0).getTime();
  const tb = new Date(b.updatedAt ?? b.createdAt ?? 0).getTime();
  return tb - ta;
}

function describeError(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    if (err.status === 0) return 'Network error — check your connection and try again.';
    if (err.status === 401) return 'Your session expired. Sign in again to continue.';
    if (err.status === 402 || err.status === 403) {
      return 'Out of credits or not authorized for this model.';
    }
    if (err.status === 429) return 'Rate limited — please wait a moment and retry.';
    if (err.status >= 500) return 'The server hit an error. Try again in a moment.';
    const detail = (err.error as { detail?: string; title?: string } | null) ?? null;
    return detail?.detail ?? detail?.title ?? err.message ?? 'Request failed.';
  }
  return err instanceof Error ? err.message : 'Something went wrong.';
}

function providerFromId(id: string): string | undefined {
  const slash = id.indexOf('/');
  return slash > 0 ? id.slice(0, slash) : undefined;
}
