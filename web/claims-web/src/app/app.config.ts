import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { apiInterceptors } from './core/http/interceptors';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    // XHR backend (not withFetch): fetch can't report upload progress, which the Documents tab shows (S11.3).
    provideHttpClient(withInterceptors(apiInterceptors)),
  ],
};
