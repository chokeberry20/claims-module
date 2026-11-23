import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { APP_CONFIG, loadAppConfig } from './app/core/config/app-config';

loadAppConfig()
  .then((config) =>
    bootstrapApplication(App, {
      ...appConfig,
      providers: [...appConfig.providers, { provide: APP_CONFIG, useValue: config }],
    }),
  )
  .catch((err: unknown) => {
    console.error(err);
    document.body.textContent = 'The application could not start: configuration is missing or invalid.';
  });
