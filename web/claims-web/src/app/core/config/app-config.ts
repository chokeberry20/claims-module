import { InjectionToken } from '@angular/core';

/**
 * Runtime configuration, loaded from /app-config.json before the app bootstraps.
 * The same build runs locally and on Azure: the deploy pipeline overwrites app-config.json
 * with the API URL of the target environment, so nothing environment-specific is compiled in.
 */
export interface AppConfig {
  /** Absolute base URL of the Claims API, without a trailing slash. */
  readonly apiBaseUrl: string;
}

export const APP_CONFIG = new InjectionToken<AppConfig>('APP_CONFIG');

export function parseAppConfig(raw: unknown): AppConfig {
  const apiBaseUrl = (raw as { apiBaseUrl?: unknown } | null)?.apiBaseUrl;
  if (typeof apiBaseUrl !== 'string' || !/^https?:\/\/\S+$/.test(apiBaseUrl)) {
    throw new Error('app-config.json: "apiBaseUrl" must be an absolute http(s) URL.');
  }
  return { apiBaseUrl: apiBaseUrl.replace(/\/+$/, '') };
}

export async function loadAppConfig(url = '/app-config.json'): Promise<AppConfig> {
  const response = await fetch(url, { cache: 'no-store' });
  if (!response.ok) {
    throw new Error(`Could not load ${url} (HTTP ${response.status}).`);
  }
  return parseAppConfig(await response.json());
}
