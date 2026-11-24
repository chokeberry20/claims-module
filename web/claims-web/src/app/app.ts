import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatToolbarModule } from '@angular/material/toolbar';
import { AuthService } from './core/auth/auth.service';
import { LoadingService } from './core/ui/loading.service';
import { ApiStatus } from './layout/api-status/api-status';
import { UserMenu } from './layout/user-menu/user-menu';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, MatToolbarModule, MatIconModule, MatProgressBarModule, ApiStatus, UserMenu],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly loading = inject(LoadingService);
  private readonly auth = inject(AuthService);
  protected readonly userKey = computed(() => this.auth.session()?.userName ?? '');
}
