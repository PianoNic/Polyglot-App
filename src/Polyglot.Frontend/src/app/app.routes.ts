import { Routes } from '@angular/router';
import { autoLoginPartialRoutesGuard } from 'angular-auth-oidc-client';
import { AppLayout } from './shared/layouts/app-layout/app-layout';
import { Chat } from './chat/chat';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'chat' },
  {
    path: '',
    component: AppLayout,
    canActivateChild: [autoLoginPartialRoutesGuard],
    children: [
      { path: 'chat', component: Chat },
      { path: 'chat/:id', component: Chat },
      { path: 'admin', loadComponent: () => import('./admin/admin').then((m) => m.Admin) },
    ],
  },
  { path: '**', redirectTo: 'chat' },
];
