import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthResponse, User } from './models';

const STORAGE_KEY = 'taskflow.auth';

/**
 * Keeps the JWT and the current user. The token is stored in localStorage so a page refresh
 * keeps you logged in. Logging out just forgets the token (JWTs are stateless).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly url = `${environment.apiUrl}/api/auth`;

  private readonly session = signal<AuthResponse | null>(this.loadSession());

  readonly user = computed<User | null>(() => this.session()?.user ?? null);
  readonly isLoggedIn = computed(() => this.session() !== null);

  get token(): string | null {
    return this.session()?.token ?? null;
  }

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.url}/login`, { email, password }).pipe(tap((r) => this.save(r)));
  }

  register(email: string, password: string, displayName: string): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${this.url}/register`, { email, password, displayName })
      .pipe(tap((r) => this.save(r)));
  }

  /** "Try the demo": the server logs us into the shared demo account. */
  demo(): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.url}/demo`, {}).pipe(tap((r) => this.save(r)));
  }

  /** Returns a new token too, because the display name is inside the JWT. */
  updateProfile(displayName: string): Observable<AuthResponse> {
    return this.http.put<AuthResponse>(`${this.url}/profile`, { displayName }).pipe(tap((r) => this.save(r)));
  }

  changePassword(currentPassword: string, newPassword: string): Observable<void> {
    return this.http.post<void>(`${this.url}/change-password`, { currentPassword, newPassword });
  }

  logout(): void {
    localStorage.removeItem(STORAGE_KEY);
    this.session.set(null);
    this.router.navigateByUrl('/login');
  }

  private save(response: AuthResponse): void {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(response));
    this.session.set(response);
  }

  /** Restores the session after a refresh, unless the token has expired. */
  private loadSession(): AuthResponse | null {
    try {
      const saved = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? 'null') as AuthResponse | null;
      if (!saved || isExpired(saved.token)) {
        localStorage.removeItem(STORAGE_KEY);
        return null;
      }
      return saved;
    } catch {
      return null;
    }
  }
}

/** Reads the "exp" claim from the JWT payload (the middle, base64url-encoded part). */
function isExpired(token: string): boolean {
  try {
    const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')));
    return typeof payload.exp !== 'number' || payload.exp * 1000 < Date.now();
  } catch {
    return true;
  }
}
