import { Routes } from '@angular/router';
import { autoLoginPartialRoutesGuard } from 'angular-auth-oidc-client';
import { AppLayout } from './layouts/app-layout/app-layout';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'chat' },
  {
    path: '',
    component: AppLayout,
    canActivateChild: [autoLoginPartialRoutesGuard],
    children: [
      {
        path: 'chat',
        loadComponent: () => import('./chat/chat').then((m) => m.Chat),
      },
    ],
  },
  { path: '**', redirectTo: 'chat' },
];
