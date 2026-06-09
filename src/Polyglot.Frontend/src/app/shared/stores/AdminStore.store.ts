import { inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  patchState,
  signalStore,
  withComputed,
  withHooks,
  withMethods,
  withState,
} from '@ngrx/signals';
import { AdminService } from '../../api/api/admin.service';
import type { AdminSettingsDto } from '../../api/model/adminSettingsDto';
import type { AvailableModelDto } from '../../api/model/availableModelDto';
import type { CreditAdjustmentMode } from '../../api/model/creditAdjustmentMode';
import type { ModelListEntryDto } from '../../api/model/modelListEntryDto';
import type { ModelListType } from '../../api/model/modelListType';
import type { UpdateAdminSettingsCommand } from '../../api/model/updateAdminSettingsCommand';
import type { UserDto } from '../../api/model/userDto';

type AdminStoreState = {
  users: UserDto[];
  usersLoaded: boolean;
  allModels: AvailableModelDto[];
  listEntries: ModelListEntryDto[];
  modelsLoaded: boolean;
  settings: AdminSettingsDto | null;
};

export const initialAdminStore: AdminStoreState = {
  users: [],
  usersLoaded: false,
  allModels: [],
  listEntries: [],
  modelsLoaded: false,
  settings: null,
};

export const AdminStore = signalStore(
  { providedIn: 'root' },
  withState(initialAdminStore),
  withComputed(() => ({})),
  withMethods((store) => {
    const adminApi = inject(AdminService);

    async function loadUsers(force = false): Promise<void> {
      if (store.usersLoaded() && !force)
        return;
      patchState(store, { usersLoaded: true });
      try {
        const users = await firstValueFrom(adminApi.apiAdminUsersGet());
        patchState(store, { users });
      } catch (err) {
        patchState(store, { usersLoaded: false });
        throw err;
      }
    }

    async function setUserLock(id: string, isLocked: boolean): Promise<void> {
      await firstValueFrom(adminApi.apiAdminUsersIdLockPut(id, { isLocked }));
      patchState(store, {
        users: store.users().map((u) => (u.id === id ? { ...u, isLocked } : u)),
      });
    }

    async function adjustCredits(id: string, amount: number, mode: CreditAdjustmentMode): Promise<void> {
      await firstValueFrom(adminApi.apiAdminUsersIdCreditsPut(id, { amount, mode }));
      await loadUsers(true);
    }

    async function loadModels(force = false): Promise<void> {
      if (store.modelsLoaded() && !force)
        return;
      patchState(store, { modelsLoaded: true });
      try {
        const [allModels, listEntries] = await Promise.all([
          firstValueFrom(adminApi.apiAdminAllModelsGet()),
          firstValueFrom(adminApi.apiAdminModelsGet()),
        ]);
        patchState(store, { allModels, listEntries });
      } catch (err) {
        patchState(store, { modelsLoaded: false });
        throw err;
      }
    }

    async function addListEntry(modelId: string, listType: ModelListType): Promise<void> {
      const entry = await firstValueFrom(adminApi.apiAdminModelsPost({ modelId, listType }));
      patchState(store, { listEntries: [...store.listEntries(), entry] });
    }

    async function removeListEntry(id: string): Promise<void> {
      await firstValueFrom(adminApi.apiAdminModelsIdDelete(id));
      patchState(store, { listEntries: store.listEntries().filter((e) => e.id !== id) });
    }

    async function loadSettings(): Promise<void> {
      if (store.settings() !== null)
        return;
      const settings = await firstValueFrom(adminApi.apiAdminSettingsGet());
      patchState(store, { settings });
    }

    async function saveSettings(command: UpdateAdminSettingsCommand): Promise<void> {
      const settings = await firstValueFrom(adminApi.apiAdminSettingsPut(command));
      patchState(store, { settings });
    }

    return {
      loadUsers,
      setUserLock,
      adjustCredits,
      loadModels,
      addListEntry,
      removeListEntry,
      loadSettings,
      saveSettings,
    };
  }),
  withHooks(() => ({})),
);
