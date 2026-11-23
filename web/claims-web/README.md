# claims-web

Angular 22 (standalone components, zoneless, Material 3) frontend for the Claims Module.

| Command | What it does |
|---|---|
| `npm ci` | Install exact dependencies from `package-lock.json` |
| `npm start` | Dev server on http://localhost:4200 |
| `npx ng build` | Production build to `dist/claims-web/browser` |
| `npx ng test --watch=false` | Unit tests (Vitest) |

**Runtime configuration.** `public/app-config.json` holds `apiBaseUrl` and is loaded before bootstrap, so one build
serves every environment. Locally it points at the API on `http://localhost:5180`; the deploy pipeline overwrites it
with the Azure API URL. The API must allow the frontend origin in `Cors:AllowedOrigins`.

`public/staticwebapp.config.json` configures Azure Static Web Apps (SPA fallback, no caching of `app-config.json`).
