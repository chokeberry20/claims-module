import { ChangeDetectionStrategy, Component } from '@angular/core';

/**
 * An empty stop on the way back to the same page after a role switch: leaving and re-entering the route
 * re-creates the page, so it loads its data again with the new user's token and role.
 */
@Component({
  selector: 'app-switching',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '',
})
export class Switching {}
