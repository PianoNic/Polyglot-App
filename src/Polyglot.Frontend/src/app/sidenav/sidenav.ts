import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideMessageCircle,
  lucideShield,
  lucideChevronsUpDown,
  lucideCoins,
  lucideInfo,
  lucideLogOut,
  lucideSun,
  lucideMoon,
  lucideMonitor,
  lucideSquarePen,
} from '@ng-icons/lucide';
import { ThemeService, ThemeMode } from '../shared/services/theme.service';
import { HlmSidebarImports, HlmSidebarService } from '@spartan-ng/helm/sidebar';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { AppService } from '../api/api/app.service';
import type { AppDto } from '../api/model/appDto';
import { ChatStore } from '../shared/stores/ChatStore.store';
import { UserStore } from '../shared/stores/UserStore.store';
import { PkConversationList } from '../../../libs/prompt-kit/conversation-list/pk-conversation-list';
import type { ConversationRename } from '../../../libs/prompt-kit/conversation-list/pk-conversation-item';

@Component({
  selector: 'polyglot-sidenav',
  imports: [
    HlmSidebarImports,
    HlmDialogImports,
    HlmDropdownMenuImports,
    HlmAvatarImports,
    NgIcon,
    RouterLink,
    PkConversationList,
    DecimalPipe,
  ],
  providers: [
    provideIcons({
      lucideMessageCircle,
      lucideShield,
      lucideChevronsUpDown,
      lucideCoins,
      lucideInfo,
      lucideLogOut,
      lucideSun,
      lucideMoon,
      lucideMonitor,
      lucideSquarePen,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sidenav.html',
})
export class Sidenav implements OnInit {
  private readonly sidebarService = inject(HlmSidebarService);
  private readonly oidcSecurityService = inject(OidcSecurityService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly theme = inject(ThemeService);
  private readonly router = inject(Router);
  private readonly appApi = inject(AppService);
  protected readonly store = inject(ChatStore);
  protected readonly userStore = inject(UserStore);

  protected readonly appInfo = signal<AppDto | null>(null);
  protected readonly creditsInfoState = signal<'open' | 'closed'>('closed');

  ngOnInit(): void {
    void this.userStore.load();
    void this.store.loadChats();
    this.appApi
      .apiAppGet()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((app) => this.appInfo.set(app));
  }

  protected readonly themeMode = this.theme.mode;
  protected readonly themeOptions: ReadonlyArray<{ mode: ThemeMode; label: string; icon: string }> = [
    { mode: 'light', label: 'Light', icon: 'lucideSun' },
    { mode: 'dark', label: 'Dark', icon: 'lucideMoon' },
    { mode: 'system', label: 'System', icon: 'lucideMonitor' },
  ];
  protected readonly menuSide = computed(() =>
    this.sidebarService.isMobile() ? 'top' : 'right'
  );

  private readonly userData = this.oidcSecurityService.userData;
  protected readonly user = computed(() => {
    const data = this.userData().userData;
    return {
      name: data?.preferred_username ?? data?.email ?? '',
      email: data?.email ?? '',
      avatar: data?.picture ?? '',
    };
  });

  protected setTheme(mode: ThemeMode): void {
    this.theme.set(mode);
  }

  protected logout(): void {
    this.oidcSecurityService
      .logoffAndRevokeTokens()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe();
  }

  protected newChat(): void {
    this.store.newChat();
    void this.router.navigate(['/chat']);
  }

  protected openConversation(id: string): void {
    void this.router.navigate(['/chat', id]);
  }

  protected renameConversation(event: ConversationRename): void {
    void this.store.renameChat(event.id, event.title);
  }

  protected deleteConversation(id: string): void {
    void this.store.deleteChat(id);
    if (this.store.activeChatId() === null) {
      void this.router.navigate(['/chat']);
    }
  }
}
