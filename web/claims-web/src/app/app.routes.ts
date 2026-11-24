import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

/** Feature areas are lazy-loaded (D-37, S11.4): ClaimsList, FnolIntake and ClaimDetail each load on first use. */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'claims' },
  {
    path: 'sign-in',
    loadComponent: () => import('./features/sign-in/sign-in').then((m) => m.SignIn),
    title: 'Sign in · Claims Module',
  },
  { path: 'switching', loadComponent: () => import('./layout/switching').then((m) => m.Switching) },
  {
    path: 'claims',
    canActivate: [authGuard],
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () => import('./features/claims-list/claims-list').then((m) => m.ClaimsList),
        title: 'Claims · Claims Module',
      },
      {
        path: 'new',
        loadComponent: () => import('./features/fnol-intake/fnol-intake').then((m) => m.FnolIntake),
        title: 'Log new claim · Claims Module',
      },
      {
        path: ':claimId',
        loadComponent: () => import('./features/claim-detail/claim-detail').then((m) => m.ClaimDetail),
        title: 'Claim · Claims Module',
      },
    ],
  },
  { path: '**', redirectTo: 'claims' },
];
