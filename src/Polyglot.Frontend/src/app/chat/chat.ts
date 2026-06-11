import { ChangeDetectionStrategy, Component, computed, effect, ElementRef, inject, OnInit, signal, untracked, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { distinctUntilChanged, map } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowUp,
  lucideLightbulb,
  lucideCode,
  lucideBookOpen,
  lucideSparkles,
  lucidePaperclip,
  lucideMic,
  lucideChevronDown,
  lucideGlobe,
} from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmIcon } from '@spartan-ng/helm/icon';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { MessageRole } from '../api/model/messageRole';
import type { AttachmentDto } from '../api/model/attachmentDto';
import type { MessageDto } from '../api/model/messageDto';
import { BASE_PATH } from '../api/variables';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { PkAttachmentChip } from '../../../libs/prompt-kit/attachment-preview';
import type { Attachment } from '../../../libs/prompt-kit/attachment-preview';
import { PkChainOfThoughtImports } from '../../../libs/prompt-kit/chain-of-thought';
import { PkChatContainerImports } from '../../../libs/prompt-kit/chat-container';
import { PkCodeBlockImports } from '../../../libs/prompt-kit/code-block';
import { PkChatEmpty } from '../../../libs/prompt-kit/chat-empty/pk-chat-empty';
import type { ChatEmptySuggestion } from '../../../libs/prompt-kit/chat-empty/pk-chat-empty';
import { PkLoader } from '../../../libs/prompt-kit/loader/pk-loader';
import { PkMessageImports } from '../../../libs/prompt-kit/message';
import { PkModelList } from '../../../libs/prompt-kit/model-list/pk-model-list';
import { PkPromptInputImports } from '../../../libs/prompt-kit/prompt-input';
import { PkResponseStream } from '../../../libs/prompt-kit/response-stream';
import { PkScrollButton } from '../../../libs/prompt-kit/scroll-button/pk-scroll-button';
import { PkSystemMessage } from '../../../libs/prompt-kit/system-message/pk-system-message';
import { PkTokenCounter } from '../../../libs/prompt-kit/token-counter/pk-token-counter';
import { ChatStore } from '../shared/stores/ChatStore.store';
import type { ToolStep } from '../shared/stores/ChatStore.store';

const SUGGESTIONS: ChatEmptySuggestion[] = [
  { label: 'Explain a concept', icon: 'lucideLightbulb', prompt: 'Explain how OAuth 2.0 works.' },
  { label: 'Review code', icon: 'lucideCode', prompt: 'Review this snippet for issues:\n\n' },
  { label: 'Summarise', icon: 'lucideBookOpen', prompt: 'Summarise the key points of: ' },
  { label: 'Brainstorm', icon: 'lucideSparkles', prompt: 'Brainstorm 5 ideas for ' },
];

@Component({
  selector: 'polyglot-chat',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  imports: [
    NgIcon,
    HlmButton,
    HlmIcon,
    PkChainOfThoughtImports,
    PkChatContainerImports,
    PkCodeBlockImports,
    PkChatEmpty,
    PkLoader,
    PkMessageImports,
    PkModelList,
    PkAttachmentChip,
    PkPromptInputImports,
    PkResponseStream,
    HlmPopoverImports,
    ContentHeader,
    PkScrollButton,
    PkSystemMessage,
    PkTokenCounter,
  ],
  providers: [
    provideIcons({
      lucideArrowUp,
      lucideLightbulb,
      lucideCode,
      lucideBookOpen,
      lucideSparkles,
      lucidePaperclip,
      lucideMic,
      lucideChevronDown,
      lucideGlobe,
    }),
  ],
  templateUrl: './chat.html',
})
export class Chat implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);
  private readonly basePath = inject(BASE_PATH);
  protected readonly store = inject(ChatStore);
  protected readonly Role = MessageRole;

  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  protected readonly acceptTypes = computed(() => {
    const documents = '.pdf,.txt,.md,.csv';
    const model = this.store.activeModel();
    return model?.inputModalities?.includes('image') ? `image/*,${documents}` : documents;
  });

  protected readonly draft = signal('');
  private readonly lastFailed = signal<string | null>(null);
  protected readonly suggestions = SUGGESTIONS;
  protected readonly inputLimit = 4000;
  protected readonly modelMenuState = signal<'open' | 'closed'>('closed');

  protected readonly activeModelLabel = computed(() => {
    const id = this.store.selectedModelId();
    if (!id) return 'Select model';
    return this.store.models().find((m) => m.id === id)?.name ?? id;
  });

  protected readonly hasMessages = computed(() => this.store.messages().length > 0);
  protected readonly canSend = computed(() => {
    const d = this.draft();
    return (
      d.trim().length > 0 &&
      d.length <= this.inputLimit &&
      !this.store.isSending() &&
      !this.store.streamingText() &&
      !!this.store.selectedModelId()
    );
  });

  private readonly routeId = toSignal(
    this.route.paramMap.pipe(
      map((p) => p.get('id')),
      distinctUntilChanged(),
    ),
    { initialValue: null },
  );

  constructor() {
    effect(() => {
      const id = this.routeId();
      untracked(() => {
        if (id) {
          void this.store.openChat(id);
        } else {
          this.store.newChat();
        }
      });
    });

  }

  ngOnInit(): void {
    void this.store.loadChats();
    void this.store.loadModels();
  }

  protected onModelChanged(id: string | null): void {
    if (id) {
      this.store.setSelectedModel(id);
      this.modelMenuState.set('closed');
    }
  }

  protected onSuggestion(s: ChatEmptySuggestion): void {
    this.draft.set(s.prompt);
  }

  protected async onSubmit(): Promise<void> {
    if (!this.canSend()) return;
    const text = this.draft();
    this.draft.set('');
    const result = await this.store.sendMessage(text);
    if (result.kind === 'error') {
      this.lastFailed.set(text);
      this.draft.set(text);
      return;
    }
    this.lastFailed.set(null);
    if (result.newId) {
      void this.router.navigate(['/chat', result.newId]);
    }
  }

  protected async retry(): Promise<void> {
    const text = this.lastFailed();
    if (!text)
      return;
    this.store.clearSendError();
    this.draft.set(text);
    await this.onSubmit();
  }

  protected openFilePicker(): void {
    this.fileInput()?.nativeElement.click();
  }

  protected async onFilesSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    for (const file of Array.from(input.files ?? [])) {
      await this.store.uploadAttachment(file);
    }
    input.value = '';
  }

  /** Parsed Message.toolCalls JSON, cached per message id. */
  private readonly toolStepsCache = new Map<string, ToolStep[]>();

  protected toolSteps(m: MessageDto): ToolStep[] {
    if (!m.toolCalls) return [];
    let steps = this.toolStepsCache.get(m.id);
    if (!steps) {
      try {
        steps = JSON.parse(m.toolCalls) as ToolStep[];
      } catch {
        steps = [];
      }
      this.toolStepsCache.set(m.id, steps);
    }
    return steps;
  }

  /** The executed code for execute_javascript steps, else the raw input JSON. */
  protected toolInput(step: ToolStep): string {
    try {
      const args = JSON.parse(step.input) as Record<string, unknown>;
      if (typeof args['code'] === 'string') return args['code'];
    } catch {
      // fall through to raw input
    }
    return step.input;
  }

  protected asChip(a: AttachmentDto): Attachment {
    return {
      id: a.id,
      name: a.fileName,
      type: a.mediaType.startsWith('image/') ? 'image' : 'file',
      size: a.sizeBytes,
      mimeType: a.mediaType,
    };
  }

  protected async openAttachment(id: string): Promise<void> {
    const blob = await firstValueFrom(
      this.http.get(`${this.basePath}/api/Attachment/${id}`, { responseType: 'blob' }),
    );
    window.open(URL.createObjectURL(blob), '_blank');
  }
}
