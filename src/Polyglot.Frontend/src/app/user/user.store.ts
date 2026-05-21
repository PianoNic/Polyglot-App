import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { UserService } from '../api/api/user.service';
import type { UserDto } from '../api/model/userDto';

@Injectable({ providedIn: 'root' })
export class UserStore {
  private readonly _api = inject(UserService);

  readonly currentUser = signal<UserDto | null>(null);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly creditBalance = computed(() => this.currentUser()?.creditBalance ?? 0);

  private _loaded = false;

  async load(force = false): Promise<void> {
    if ((this._loaded && !force) || this.isLoading()) return;
    this.isLoading.set(true);
    this.loadError.set(null);
    try {
      const me = await firstValueFrom(this._api.apiUserMeGet());
      this.currentUser.set(me);
      this._loaded = true;
    } catch (err) {
      this.loadError.set(err instanceof Error ? err.message : 'Failed to load user.');
    } finally {
      this.isLoading.set(false);
    }
  }
}
