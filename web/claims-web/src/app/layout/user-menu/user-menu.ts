import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { Router } from '@angular/router';
import { DemoUser } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';

/**
 * Header: the signed-in user's name and role, and the role switcher (S11.4). Switching re-uses a token
 * already issued in this tab; otherwise it opens the sign-in form for the chosen user (D-73).
 */
@Component({
  selector: 'app-user-menu',
  imports: [MatButtonModule, MatMenuModule, MatIconModule, MatDividerModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (auth.session(); as session) {
      <button mat-button class="user-button" [matMenuTriggerFor]="menu" data-testid="user-menu">
        <mat-icon>account_circle</mat-icon>
        <span class="name" data-testid="current-user">{{ session.displayName }}</span>
        <span class="role" data-testid="current-role">{{ session.role }}</span>
      </button>
      <mat-menu #menu="matMenu" xPosition="before">
        <div mat-menu-item disabled class="menu-title">Switch user</div>
        @for (user of users(); track user.userName) {
          <button
            mat-menu-item
            [disabled]="user.userName === session.userName"
            (click)="switchTo(user)"
            [attr.data-testid]="'switch-' + user.userName"
          >
            <mat-icon>{{ auth.hasValidToken(user.userName) ? 'how_to_reg' : 'login' }}</mat-icon>
            <span>{{ user.displayName }} · {{ user.role }}</span>
          </button>
        }
        <mat-divider />
        <button mat-menu-item (click)="signOut()" data-testid="sign-out">
          <mat-icon>logout</mat-icon>
          <span>Sign out</span>
        </button>
      </mat-menu>
    }
  `,
  styles: `
    .user-button { color: inherit; }
    .name { margin: 0 8px 0 4px; }
    .role {
      font: var(--mat-sys-label-small);
      padding: 2px 8px;
      border-radius: 10px;
      background: color-mix(in srgb, currentColor 18%, transparent);
    }
    .menu-title { font: var(--mat-sys-label-medium); }
  `,
})
export class UserMenu {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly users = signal<DemoUser[]>([]);

  constructor() {
    // Load the directory once someone is signed in (the list itself is anonymous).
    effect(() => {
      if (this.auth.isSignedIn() && this.users().length === 0) {
        // A failure shows the snackbar (error interceptor); the menu then only offers Sign out.
        this.auth.users().subscribe({ next: (users) => this.users.set(users), error: () => undefined });
      }
    });
  }

  protected switchTo(user: DemoUser): void {
    const returnUrl = this.router.url;
    if (!this.auth.switchTo(user.userName)) {
      void this.router.navigate(['/sign-in'], { queryParams: { user: user.userName, returnUrl } });
      return;
    }
    // Re-create the current page so it reloads with the new user's token and role.
    void this.router
      .navigateByUrl('/switching', { skipLocationChange: true })
      .then(() => this.router.navigateByUrl(returnUrl));
  }

  protected signOut(): void {
    this.auth.signOut();
    void this.router.navigate(['/sign-in']);
  }
}
