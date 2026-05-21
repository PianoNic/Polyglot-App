import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { HlmIcon } from '@spartan-ng/helm/icon';
import {
  lucideMessageCircle,
  lucideShield,
  lucideChevronsUpDown,
  lucideSparkles,
  lucideBadgeCheck,
  lucideCreditCard,
  lucideBell,
  lucideLogOut,
  lucideSun,
  lucideMoon,
  lucideMonitor,
  lucideSquarePen,
} from '@ng-icons/lucide';
import { ThemeService, ThemeMode } from '../theme';
import { HlmSidebarImports, HlmSidebarService } from '@spartan-ng/helm/sidebar';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { HlmButton } from '@spartan-ng/helm/button';
import { ChatStore } from '../chat/chat.store';
import { UserStore } from '../user/user.store';
import { PkConversationList } from '../../../libs/prompt-kit/conversation-list/pk-conversation-list';
import type { ConversationRename } from '../../../libs/prompt-kit/conversation-list/pk-conversation-item';

@Component({
  selector: 'polyglot-sidenav',
  imports: [
    HlmSidebarImports,
    HlmDropdownMenuImports,
    HlmAvatarImports,
    HlmButton,
    NgIcon,
    HlmIcon,
    RouterLink,
    PkConversationList,
    DecimalPipe,
  ],
  providers: [
    provideIcons({
      lucideMessageCircle,
      lucideShield,
      lucideChevronsUpDown,
      lucideSparkles,
      lucideBadgeCheck,
      lucideCreditCard,
      lucideBell,
      lucideLogOut,
      lucideSun,
      lucideMoon,
      lucideMonitor,
      lucideSquarePen,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sidenav.html',
  styleUrl: './sidenav.css',
})
export class Sidenav {
  private readonly _sidebarService = inject(HlmSidebarService);
  private readonly _oidcSecurityService = inject(OidcSecurityService);
  private readonly _destroyRef = inject(DestroyRef);
  private readonly _theme = inject(ThemeService);
  private readonly _router = inject(Router);
  protected readonly store = inject(ChatStore);
  protected readonly userStore = inject(UserStore);

  constructor() {
    void this.userStore.load();
    void this.store.loadChats();
  }

  protected readonly themeMode = this._theme.mode;
  protected readonly _themeOptions: ReadonlyArray<{ mode: ThemeMode; label: string; icon: string }> = [
    { mode: 'light', label: 'Light', icon: 'lucideSun' },
    { mode: 'dark', label: 'Dark', icon: 'lucideMoon' },
    { mode: 'system', label: 'System', icon: 'lucideMonitor' },
  ];
  protected readonly _menuSide = computed(() =>
    this._sidebarService.isMobile() ? 'top' : 'right'
  );

  private readonly _userData = this._oidcSecurityService.userData;
  protected readonly _user = computed(() => {
    const data = this._userData().userData;
    return {
      name: data?.preferred_username ?? data?.email ?? '',
      email: data?.email ?? '',
      avatar: data?.picture ?? '',
    };
  });

  protected setTheme(mode: ThemeMode): void {
    this._theme.set(mode);
  }

  protected logout(): void {
    this._oidcSecurityService
      .logoffAndRevokeTokens()
      .pipe(takeUntilDestroyed(this._destroyRef))
      .subscribe();
  }

  protected newChat(): void {
    this.store.newChat();
    void this._router.navigate(['/chat']);
  }

  protected openConversation(id: string): void {
    void this._router.navigate(['/chat', id]);
  }

  protected renameConversation(event: ConversationRename): void {
    void this.store.renameChat(event.id, event.title);
  }

  protected deleteConversation(id: string): void {
    void this.store.deleteChat(id);
    if (this.store.activeChatId() === null) {
      void this._router.navigate(['/chat']);
    }
  }
}
