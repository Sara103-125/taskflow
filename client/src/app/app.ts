import { Component, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';
import { ToastService } from './shared/toast.service';

/** App shell: top bar, the current page, and toast messages. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink],
  template: `
    <header class="topbar">
      <a class="logo" routerLink="/boards">Task<span>Flow</span></a>
      @if (auth.user(); as user) {
        <div class="topbar-user">
          <span class="muted">{{ user.displayName }}</span>
          <button class="btn btn-ghost" (click)="auth.logout()">Log out</button>
        </div>
      }
    </header>

    <main>
      <router-outlet />
    </main>

    <div class="toasts" aria-live="polite">
      @for (toast of toasts.toasts(); track toast.id) {
        <div class="toast" [class.toast-error]="toast.kind === 'error'" (click)="toasts.dismiss(toast.id)">
          {{ toast.text }}
        </div>
      }
    </div>
  `,
})
export class App {
  protected readonly auth = inject(AuthService);
  protected readonly toasts = inject(ToastService);
}
