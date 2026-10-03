import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';
import { Notification } from './core/models';
import { NotificationService } from './core/notification.service';
import { ThemeService } from './core/theme.service';
import { TimeAgoPipe } from './shared/format';
import { ToastService } from './shared/toast.service';

/** App shell: top bar (navigation, notifications, theme), the current page, and toast messages. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TimeAgoPipe],
  template: `
    <header class="topbar">
      <a class="logo" routerLink="/boards">Task<span>Flow</span></a>
      @if (auth.user(); as user) {
        <nav class="topnav">
          <a routerLink="/boards" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">Boards</a>
          <a routerLink="/my-tasks" routerLinkActive="active">My tasks</a>
        </nav>
      }
      <div class="topbar-user">
        <button class="icon-btn theme-toggle" (click)="theme.toggle()" [title]="theme.theme() === 'dark' ? 'Light mode' : 'Dark mode'">
          {{ theme.theme() === 'dark' ? '&#9728;' : '&#9790;' }}
        </button>
        @if (auth.user(); as user) {
          <div class="bell-wrap">
            <button class="icon-btn bell" (click)="bellOpen.set(!bellOpen())" title="Notifications">
              &#128276;
              @if (notifications.unreadCount()) {
                <span class="bell-count">{{ notifications.unreadCount() }}</span>
              }
            </button>
            @if (bellOpen()) {
              <div class="bell-backdrop" (click)="bellOpen.set(false)"></div>
              <div class="card bell-panel">
                <div class="bell-head">
                  <strong>Notifications</strong>
                  <span class="spacer"></span>
                  @if (notifications.unreadCount()) {
                    <button class="btn btn-ghost btn-small" (click)="notifications.markAllRead()">Mark all read</button>
                  }
                </div>
                <ul>
                  @for (n of notifications.items(); track n.id) {
                    <li [class.unread]="!n.isRead" (click)="open(n)">
                      <div>{{ n.message }}</div>
                      <div class="muted small">{{ n.createdAt | timeAgo }}</div>
                    </li>
                  } @empty {
                    <li class="muted">You're all caught up.</li>
                  }
                </ul>
              </div>
            }
          </div>
          <a class="muted profile-link" routerLink="/profile" title="Profile">{{ user.displayName }}</a>
          <button class="btn btn-ghost" (click)="auth.logout()">Log out</button>
        }
      </div>
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
  protected readonly notifications = inject(NotificationService);
  protected readonly theme = inject(ThemeService);
  private readonly router = inject(Router);

  protected readonly bellOpen = signal(false);

  /** Clicking a notification opens the board (and the task, if there is one). */
  protected open(n: Notification): void {
    this.notifications.markRead(n);
    this.bellOpen.set(false);
    this.router.navigate(['/boards', n.boardId], { queryParams: n.taskId ? { task: n.taskId } : {} });
  }
}
