import { Routes } from '@angular/router';

/** Feature areas are lazy-loaded route groups (D-37). P6a adds claims list, FNOL and claim detail. */
export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./features/home/home').then((m) => m.Home),
    title: 'Claims Module',
  },
  { path: '**', redirectTo: '' },
];
