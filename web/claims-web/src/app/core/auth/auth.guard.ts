import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Pages other than sign-in need a signed-in user; otherwise go to the sign-in form and come back. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  if (auth.accessToken()) {
    return true;
  }
  return inject(Router).createUrlTree(['/sign-in'], { queryParams: { returnUrl: state.url } });
};
