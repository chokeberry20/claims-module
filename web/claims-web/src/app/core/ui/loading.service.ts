import { computed, Injectable, signal } from '@angular/core';

/** Counts API calls in flight; the shell shows a progress bar while any are running (B3.7.4). */
@Injectable({ providedIn: 'root' })
export class LoadingService {
  private readonly pending = signal(0);

  readonly isLoading = computed(() => this.pending() > 0);

  begin(): void {
    this.pending.update((n) => n + 1);
  }

  end(): void {
    this.pending.update((n) => Math.max(0, n - 1));
  }
}
