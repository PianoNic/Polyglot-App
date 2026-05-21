import { Component, DestroyRef, inject, OnInit } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterOutlet } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { filter, switchMap, take } from 'rxjs';
import { UserService } from './api/api/user.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
})
export class App implements OnInit {
  private readonly oidcSecurityService = inject(OidcSecurityService);
  private readonly userService = inject(UserService);
  private readonly destroyRef = inject(DestroyRef);

  ngOnInit(): void {
    this.oidcSecurityService.isAuthenticated$
      .pipe(
        filter(({ isAuthenticated }) => isAuthenticated),
        take(1),
        switchMap(() => this.userService.apiUserCallbackGet()),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
  }
}
