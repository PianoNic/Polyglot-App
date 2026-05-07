import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { HlmIcon } from '@spartan-ng/helm/icon';
import {
  lucideMessageCircle,
  lucideSettings,
  lucideShield,
  lucideChevronsUpDown,
  lucideSparkles,
  lucideBadgeCheck,
  lucideCreditCard,
  lucideBell,
  lucideLogOut,
} from '@ng-icons/lucide';
import { HlmSidebarImports, HlmSidebarService } from '@spartan-ng/helm/sidebar';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';

@Component({
  selector: 'polyglot-sidenav',
  imports: [
    HlmSidebarImports,
    HlmDropdownMenuImports,
    HlmAvatarImports,
    NgIcon,
    HlmIcon,
    RouterLink,
    RouterLinkActive,
  ],
  providers: [
    provideIcons({
      lucideMessageCircle,
      lucideSettings,
      lucideShield,
      lucideChevronsUpDown,
      lucideSparkles,
      lucideBadgeCheck,
      lucideCreditCard,
      lucideBell,
      lucideLogOut,
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
  protected readonly _menuSide = computed(() =>
    this._sidebarService.isMobile() ? 'top' : 'right'
  );

  protected readonly _items = [
    { title: 'Chat', url: '/chat', icon: 'lucideMessageCircle' },
    { title: 'Admin', url: '/admin', icon: 'lucideShield' },
  ];

  private readonly _userData = this._oidcSecurityService.userData;
  protected readonly _user = computed(() => {
    const data = this._userData().userData;
    return {
      name: data?.preferred_username ?? data?.email ?? '',
      email: data?.email ?? '',
      avatar: data?.picture ?? '',
    };
  });

  protected logout(): void {
    this._oidcSecurityService
      .logoffAndRevokeTokens()
      .pipe(takeUntilDestroyed(this._destroyRef))
      .subscribe();
  }
}
