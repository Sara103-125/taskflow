import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { BoardHubService } from './board-hub.service';
import { Notification } from './models';

/**
 * The bell icon's data. Loaded once after login, then kept up to date by the server pushing
 * "NotificationAdded" to this user (on whatever page they are).
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly api = inject(ApiService);
  private readonly hub = inject(BoardHubService);

  readonly items = signal<Notification[]>([]);
  readonly unreadCount = computed(() => this.items().filter((n) => !n.isRead).length);

  constructor() {
    const auth = inject(AuthService);
    this.hub.on<Notification>('NotificationAdded', (n) => this.items.update((list) => [n, ...list].slice(0, 30)));

    effect(() => {
      if (auth.isLoggedIn()) this.load();
      else this.items.set([]);
    });
  }

  load(): void {
    this.api.getNotifications().subscribe({ next: (r) => this.items.set(r.items), error: () => {} });
  }

  markRead(n: Notification): void {
    if (n.isRead) return;
    this.items.update((list) => list.map((x) => (x.id === n.id ? { ...x, isRead: true } : x)));
    this.api.markNotificationRead(n.id).subscribe({ error: () => {} });
  }

  markAllRead(): void {
    this.items.update((list) => list.map((x) => ({ ...x, isRead: true })));
    this.api.markAllNotificationsRead().subscribe({ error: () => {} });
  }
}
