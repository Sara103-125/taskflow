import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';

/**
 * Adds "Authorization: Bearer <token>" to every call to our API.
 * If the API answers 401 (token expired or invalid), we log the user out.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.token;
  const isApiCall = req.url.startsWith(`${environment.apiUrl}/api/`);

  if (token && isApiCall) {
    req = req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
  }

  return next(req).pipe(
    catchError((err: unknown) => {
      const isLoginAttempt = req.url.includes('/api/auth/');
      if (err instanceof HttpErrorResponse && err.status === 401 && isApiCall && !isLoginAttempt) {
        auth.logout();
      }
      return throwError(() => err);
    }),
  );
};
