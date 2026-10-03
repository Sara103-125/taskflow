import { Injectable, effect, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';

export type LiveStatus = 'connecting' | 'live' | 'reconnecting' | 'offline';

/**
 * Wraps the SignalR connection. One connection per browser tab; the server puts it into
 * a "board-{id}" group when we call JoinBoard, and then pushes that board's changes to us.
 */
@Injectable({ providedIn: 'root' })
export class BoardHubService {
  private readonly auth = inject(AuthService);
  private connection: HubConnection | null = null;
  private reconnectedHandlers: Array<() => void> = [];

  readonly status = signal<LiveStatus>('offline');

  constructor() {
    // When the user logs out (or their token expires) close the connection.
    effect(() => {
      if (!this.auth.isLoggedIn()) void this.stop();
    });
  }

  /** Starts the connection if needed. */
  async start(): Promise<void> {
    if (!this.connection) {
      this.connection = new HubConnectionBuilder()
        // Browsers can't add an Authorization header to WebSockets, so the token goes in the URL.
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

    if (this.connection.state === HubConnectionState.Disconnected) {
      this.status.set('connecting');
      await this.connection.start();
      this.status.set('live');
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

  /** Listens for a server event. Returns a function that stops listening. */
  on<T>(eventName: string, handler: (payload: T) => void): () => void {
    this.connection!.on(eventName, handler);
    return () => this.connection?.off(eventName, handler);
  }

  onReconnected(handler: () => void): () => void {
    this.reconnectedHandlers.push(handler);
    return () => (this.reconnectedHandlers = this.reconnectedHandlers.filter((h) => h !== handler));
  }

  /** Called on logout so the next user gets a fresh connection with their own token. */
  async stop(): Promise<void> {
    await this.connection?.stop();
    this.connection = null;
    this.status.set('offline');
  }
}
