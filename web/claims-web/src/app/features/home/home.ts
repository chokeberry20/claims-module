import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatCardModule } from '@angular/material/card';

/** Placeholder landing page for the walking skeleton; the claims dashboard replaces it in P6a. */
@Component({
  selector: 'app-home',
  imports: [MatCardModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>Claims Module</mat-card-title>
        <mat-card-subtitle>FNOL intake and reserve management</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        <p>Walking skeleton: the frontend is deployed and talks to the API. The API status is shown in the footer.</p>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    :host { display: block; max-width: 720px; }
  `,
})
export class Home {}
