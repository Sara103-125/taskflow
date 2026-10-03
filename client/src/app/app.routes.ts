import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'boards' },
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./pages/login/login').then((m) => m.LoginPage),
  },
  {
    path: 'boards',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/boards/boards').then((m) => m.BoardsPage),
  },
  {
    path: 'boards/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/board/board').then((m) => m.BoardPage),
  },
  { path: '**', redirectTo: 'boards' },
];
