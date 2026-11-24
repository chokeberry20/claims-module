import { ChangeDetectionStrategy, Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { Router } from '@angular/router';
import { summarizeError, toApiError } from '../../core/api/api-error';
import { DemoUser } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';

/** Only same-app paths are followed after sign-in (no open redirect). */
export function safeReturnUrl(url: string | undefined | null): string {
  return url && url.startsWith('/') && !url.startsWith('//') && !url.startsWith('/sign-in') ? url : '/claims';
}

/**
 * The only way to sign in (D-73): pick a demo user (from GET /api/auth/users) and type their password.
 * No password is stored anywhere in the app; the issued token stays in memory for this tab.
 */
@Component({
  selector: 'app-sign-in',
  imports: [
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatSelectModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <mat-card appearance="outlined" class="sign-in">
      <mat-card-header>
        <mat-card-title>Sign in</mat-card-title>
        <mat-card-subtitle>Demo users: pick a user and enter their password</mat-card-subtitle>
      </mat-card-header>
      <form [formGroup]="form" (ngSubmit)="submit()">
        <mat-card-content>
          <mat-form-field appearance="outline">
            <mat-label>User</mat-label>
            <mat-select formControlName="userName" data-testid="user-select">
              @for (user of users(); track user.userName) {
                <mat-option [value]="user.userName">{{ user.displayName }} ({{ user.role }})</mat-option>
              }
            </mat-select>
            @if (form.controls.userName.hasError('required')) {
              <mat-error>Choose a user.</mat-error>
            }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Password</mat-label>
            <input matInput type="password" formControlName="password" autocomplete="current-password" data-testid="password" />
            @if (form.controls.password.hasError('required')) {
              <mat-error>Enter the password.</mat-error>
            }
          </mat-form-field>
          @if (error(); as message) {
            <p class="form-error" role="alert" data-testid="sign-in-error">{{ message }}</p>
          }
          @if (switching()) {
            <p class="hint">Switching user: enter the password for this user. Users you already signed in as in this tab are switched without a password.</p>
          }
        </mat-card-content>
        <mat-card-actions align="end">
          <button mat-flat-button type="submit" [disabled]="busy()" data-testid="sign-in-submit">
            @if (busy()) {
              <mat-spinner diameter="18" />
            } @else {
              Sign in
            }
          </button>
        </mat-card-actions>
      </form>
    </mat-card>
  `,
  styles: `
    :host { display: flex; justify-content: center; padding-top: 48px; }
    .sign-in { width: 420px; }
    form mat-form-field { width: 100%; }
    mat-card-content { padding-top: 16px; }
    .form-error { color: var(--mat-sys-error); margin: 0 0 8px; }
    .hint { font: var(--mat-sys-body-small); opacity: 0.8; }
  `,
})
export class SignIn implements OnInit {
  /** Query parameters (component input binding). */
  readonly returnUrl = input<string>();
  readonly user = input<string>();

  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly users = signal<DemoUser[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly switching = computed(() => !!this.user());

  protected readonly form = inject(NonNullableFormBuilder).group({
    userName: ['', Validators.required],
    password: ['', Validators.required],
  });

  ngOnInit(): void {
    const preselected = this.user();
    if (preselected) {
      this.form.controls.userName.setValue(preselected);
    }
    this.auth.users().subscribe((users) => this.users.set(users));
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { userName, password } = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    this.auth.signIn(userName, password).subscribe({
      next: () => {
        this.form.controls.password.reset();
        void this.router.navigateByUrl(safeReturnUrl(this.returnUrl()));
      },
      error: (err: unknown) => {
        const body = toApiError(err);
        this.busy.set(false);
        this.form.controls.password.reset();
        this.error.set(
          body.status === 401
            ? 'Wrong user or password.'
            : body.status === 429
              ? 'Too many sign-in attempts. Wait a minute and try again.'
              : summarizeError(body),
        );
      },
    });
  }
}
