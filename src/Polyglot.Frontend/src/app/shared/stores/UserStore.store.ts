import { computed, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  patchState,
  signalStore,
  withComputed,
  withHooks,
  withMethods,
  withState,
} from '@ngrx/signals';
import { UserService } from '../../api/api/user.service';
import type { UserDto } from '../../api/model/userDto';

type UserStoreState = {
  currentUser: UserDto | null;
  loaded: boolean;
};

export const initialUserStore: UserStoreState = {
  currentUser: null,
  loaded: false,
};

export const UserStore = signalStore(
  { providedIn: 'root' },
  withState(initialUserStore),
  withComputed((store) => ({
    creditBalance: computed(() => store.currentUser()?.creditBalance ?? 0),
  })),
  withMethods((store) => {
    const userService = inject(UserService);

    async function load(): Promise<void> {
      if (store.loaded())
        return;
      const me = await firstValueFrom(userService.apiUserMeGet());
      patchState(store, { currentUser: me, loaded: true });
    }

    return { load };
  }),
  withHooks(() => ({})),
);
