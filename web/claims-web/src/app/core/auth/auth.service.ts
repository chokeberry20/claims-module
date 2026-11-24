import { HttpClient, HttpContext } from '@angular/common/http';
import { computed, inject, Injectable, InjectionToken, signal } from '@angular/core';
import { map, Observable, tap } from 'rxjs';
import { SKIP_ERROR_SNACKBAR } from '../api/api-error';
import { DemoUser, TokenResponse, UserRole } from '../api/models';
import { APP_CONFIG } from '../config/app-config';

/** A signed-in demo user and their bearer token. */
export interface Session {
  readonly userName: string;
  readonly userId: string;
  readonly displayName: string;
  readonly role: UserRole;
  readonly accessToken: string;
  /** Epoch milliseconds. */
  readonly expiresAt: number;
}

/** Current time in epoch milliseconds; replaced in tests. */
export const CLOCK = new InjectionToken<() => number>('CLOCK', { factory: () => () => Date.now() });

/** A token is treated as expired this long before its real expiry, so a call never races it. */
const EXPIRY_MARGIN_MS = 60_000;

const ROLE_RANK: Readonly<Record<UserRole, number>> = { Handler: 0, Supervisor: 1, Manager: 2 };

/** Whether `role` is at least `minimum` (handler < supervisor < manager, S3). */
export function hasRoleAtLeast(role: UserRole | undefined, minimum: UserRole): boolean {
  return role !== undefined && ROLE_RANK[role] >= ROLE_RANK[minimum];
}

/**
 * Mock authentication (S11.4) with the user's D-73 rules:
 * - demo users sign in only through the sign-in form (user picker + password); no password is ever held
 *   in code, configuration or storage;
 * - issued tokens live **in memory for this tab only** (this service), never in localStorage or
 *   sessionStorage, so a reload or a new tab starts signed out;
 * - the role switcher re-uses a token already issued in this tab until it expires, otherwise it asks
 *   for that user's password again.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(APP_CONFIG).apiBaseUrl;
  private readonly now = inject(CLOCK);

  /** Tokens issued in this tab, by user name. */
  private readonly tokens = new Map<string, Session>();
  private readonly current = signal<Session | null>(null);

  readonly session = this.current.asReadonly();
  readonly isSignedIn = computed(() => this.current() !== null);
  readonly role = computed(() => this.current()?.role);

  readonly tokenUrl = `${this.base}/api/auth/token`;

  /**
   * The demo users (names and roles, no passwords) for the sign-in form and the role switcher.
   * `inlineErrors`: the caller shows a failure itself (the sign-in form), so no snackbar.
   */
  users(options?: { inlineErrors?: boolean }): Observable<DemoUser[]> {
    const context = new HttpContext().set(SKIP_ERROR_SNACKBAR, options?.inlineErrors ?? false);
    return this.http.get<DemoUser[]>(`${this.base}/api/auth/users`, { context });
  }

  /** Signs in through the form. Errors (401, 422, 429) are shown by the form, not by a snackbar. */
  signIn(userName: string, password: string): Observable<Session> {
    return this.http
      .post<TokenResponse>(this.tokenUrl, { userName, password }, { context: new HttpContext().set(SKIP_ERROR_SNACKBAR, true) })
      .pipe(
        map((response) => toSession(userName, response)),
        tap((session) => {
          this.tokens.set(session.userName, session);
          this.current.set(session);
        }),
      );
  }

  /** Switches to a user who already signed in this tab. False when there is no unexpired token: ask for the password. */
  switchTo(userName: string): boolean {
    const cached = this.tokens.get(userName);
    if (!cached || !this.isValid(cached)) {
      this.tokens.delete(userName);
      return false;
    }
    this.current.set(cached);
    return true;
  }

  /** Whether this tab holds an unexpired token for the user (the switcher shows no password prompt then). */
  hasValidToken(userName: string): boolean {
    const cached = this.tokens.get(userName);
    return !!cached && this.isValid(cached);
  }

  /** The bearer token for API calls, or null when signed out or expired. */
  accessToken(): string | null {
    const session = this.current();
    if (!session) {
      return null;
    }
    if (!this.isValid(session)) {
      this.expireCurrent();
      return null;
    }
    return session.accessToken;
  }

  /** The API rejected the current token (401): forget it and sign out of this user. */
  expireCurrent(): void {
    const session = this.current();
    if (session) {
      this.tokens.delete(session.userName);
    }
    this.current.set(null);
  }

  /** Signs out and forgets every token issued in this tab. */
  signOut(): void {
    this.tokens.clear();
    this.current.set(null);
  }

  hasRole(minimum: UserRole): boolean {
    return hasRoleAtLeast(this.current()?.role, minimum);
  }

  private isValid(session: Session): boolean {
    return session.expiresAt - EXPIRY_MARGIN_MS > this.now();
  }
}

function toSession(userName: string, response: TokenResponse): Session {
  return {
    userName,
    userId: response.userId,
    displayName: response.displayName,
    role: response.role,
    accessToken: response.accessToken,
    expiresAt: Date.parse(response.expiresAt),
  };
}
