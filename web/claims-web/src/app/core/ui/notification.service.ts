import { inject, Injectable } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

export type NotificationSeverity = 'success' | 'info' | 'warning' | 'error';

const DURATION_MS: Readonly<Record<NotificationSeverity, number>> = {
  success: 4000,
  info: 4000,
  warning: 7000,
  error: 8000,
};

/** Material snackbars with a severity colour (S11.4 "snackbar notifications with appropriate severity"). */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly snackBar = inject(MatSnackBar);

  success(message: string): void {
    this.show(message, 'success');
  }

  info(message: string): void {
    this.show(message, 'info');
  }

  warning(message: string): void {
    this.show(message, 'warning');
  }

  error(message: string): void {
    this.show(message, 'error');
  }

  show(message: string, severity: NotificationSeverity): void {
    this.snackBar.open(message, 'Dismiss', {
      duration: DURATION_MS[severity],
      panelClass: ['claims-snack', `claims-snack-${severity}`],
      politeness: severity === 'error' ? 'assertive' : 'polite',
    });
  }
}
