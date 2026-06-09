import { ChangeDetectionStrategy, Component, computed, effect, inject, OnInit, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
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
} from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmIcon } from '@spartan-ng/helm/icon';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { MessageRole } from '../api/model/messageRole';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { PkChatContainerImports } from '../../../libs/prompt-kit/chat-container';
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
    PkChatContainerImports,
    PkChatEmpty,
    PkLoader,
    PkMessageImports,
    PkModelList,
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
    }),
  ],
  templateUrl: './chat.html',
})
export class Chat implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly store = inject(ChatStore);
  protected readonly Role = MessageRole;

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
}
