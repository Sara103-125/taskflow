import { Injectable, effect, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';

export type LiveStatus = 'connecting' | 'live' | 'reconnecting' | 'offline';

/**
 * Wraps the SignalR connection. One connection per browser tab, opened right after login.
 * - Board updates: the server puts the connection into a "board-{id}" group on JoinBoard.
 * - Notifications: the server sends them to the user directly, on any page.
 */
@Injectable({ providedIn: 'root' })
export class BoardHubService {
  private readonly auth = inject(AuthService);
  private connection: HubConnection | null = null;
  private reconnectedHandlers: Array<() => void> = [];

  readonly status = signal<LiveStatus>('offline');

  constructor() {
    // Connect when logged in; disconnect on logout or when the token expires.
    effect(() => {
      if (this.auth.isLoggedIn()) void this.start().catch(() => this.status.set('offline'));
      else void this.stop();
    });
  }

  /** Starts the connection if needed. */
  async start(): Promise<void> {
    const connection = this.getConnection();
    if (connection.state === HubConnectionState.Disconnected) {
      this.status.set('connecting');
      await connection.start();
      this.status.set('live');
    } else if (connection.state !== HubConnectionState.Connected) {
      // Already connecting: wait until it's done.
      while (connection.state === HubConnectionState.Connecting) await new Promise((r) => setTimeout(r, 50));
    }
  }

  async joinBoard(boardId: number): Promise<void> {
    await this.start();
    await this.connection!.invoke('JoinBoard', boardId);
  }

  async leaveBoard(boardId: number): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) {
      await this.connection.invoke('LeaveBoard', boardId);
    }
  }

  /** Tells teammates which task we have open (null = dialog closed). */
  setEditing(boardId: number, taskId: number | null): void {
    if (this.connection?.state === HubConnectionState.Connected) {
      this.connection.invoke('SetEditing', boardId, taskId).catch(() => {});
    }
  }

  /** Listens for a server event. Returns a function that stops listening. */
  on<T>(eventName: string, handler: (payload: T) => void): () => void {
    const connection = this.getConnection();
    connection.on(eventName, handler);
    return () => connection.off(eventName, handler);
  }

  onReconnected(handler: () => void): () => void {
    this.reconnectedHandlers.push(handler);
    return () => (this.reconnectedHandlers = this.reconnectedHandlers.filter((h) => h !== handler));
  }

  async stop(): Promise<void> {
    await this.connection?.stop();
    this.status.set('offline');
  }

  /** Builds the connection object once (it is only opened by start()). */
  private getConnection(): HubConnection {
    if (!this.connection) {
      this.connection = new HubConnectionBuilder()
        // Browsers can't add an Authorization header to WebSockets, so the token goes in the URL.
        // The factory runs on every (re)connect, so it always uses the current user's token.
        .withUrl(`${environment.apiUrl}/hubs/board`, { accessTokenFactory: () => this.auth.token ?? '' })
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build();

      this.connection.onreconnecting(() => this.status.set('reconnecting'));
      this.connection.onreconnected(() => {
        this.status.set('live');
        // A reconnect is a brand-new connection, so it is in no groups yet.
        this.reconnectedHandlers.forEach((handler) => handler());
      });
      this.connection.onclose(() => this.status.set('offline'));
    }
    return this.connection;
  }
}
