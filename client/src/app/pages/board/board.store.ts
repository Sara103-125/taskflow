import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { BoardHubService } from '../../core/board-hub.service';
import { errorMessage } from '../../core/errors';
import { Activity, BoardDetail, Column, Member, OnlineUser, Task, TaskInput } from '../../core/models';
import { ToastService } from '../../shared/toast.service';

/** Result of saving a task: either it worked, or someone else changed it first (409). */
export type SaveResult = { ok: true } | { ok: false; conflict: Task | null; message: string };

const byPosition = (a: { position: number }, b: { position: number }) => a.position - b.position;

/**
 * All state for the open board, kept in signals.
 *
 * The flow for every change is the same:
 *   1. the user acts -> we call the REST API (for drags we also update the screen immediately)
 *   2. the server saves and broadcasts an event to the board's SignalR group
 *   3. every open tab (including ours) receives the event and updates these signals
 * Applying an event twice is harmless because we always replace items by id.
 *
 * Provided by BoardPage, so each opened board gets a fresh store.
 */
@Injectable()
export class BoardStore {
  private readonly api = inject(ApiService);
  private readonly hub = inject(BoardHubService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  private boardId = 0;
  private unsubscribers: Array<() => void> = [];

  readonly loading = signal(true);
  readonly board = signal<BoardDetail | null>(null);
  readonly columns = signal<Column[]>([]);
  readonly tasks = signal<Task[]>([]);
  readonly members = signal<Member[]>([]);
  readonly activity = signal<Activity[]>([]);
  readonly online = signal<OnlineUser[]>([]);
  readonly liveStatus = this.hub.status;

  readonly isOwner = computed(() => this.board()?.myRole === 'Owner');
  readonly sortedColumns = computed(() => [...this.columns()].sort(byPosition));

  /** Tasks grouped by column and sorted, e.g. { 12: [taskA, taskB], 13: [] }. */
  readonly tasksByColumn = computed(() => {
    const groups: Record<number, Task[]> = {};
    for (const column of this.columns()) groups[column.id] = [];
    for (const task of this.tasks()) groups[task.columnId]?.push(task);
    for (const id in groups) groups[id].sort(byPosition);
    return groups;
  });

  // ---------- Loading & live connection ----------

  async open(boardId: number): Promise<void> {
    this.boardId = boardId;
    try {
      await this.reload();
      await this.hub.start();
      this.listen();
      await this.hub.joinBoard(boardId);
    } catch (err) {
      this.toast.error(err instanceof HttpErrorResponse ? errorMessage(err) : 'Live updates are unavailable right now.');
      if (err instanceof HttpErrorResponse) this.router.navigateByUrl('/boards');
    }
  }

  async close(): Promise<void> {
    this.unsubscribers.forEach((off) => off());
    this.unsubscribers = [];
    await this.hub.leaveBoard(this.boardId).catch(() => {});
  }

  /** Fetches the whole board. Used on open and after a reconnect (we may have missed events). */
  private async reload(): Promise<void> {
    const [board, activity] = await Promise.all([
      firstValueFrom(this.api.getBoard(this.boardId)),
      firstValueFrom(this.api.getActivity(this.boardId)),
    ]);
    this.board.set(board);
    this.columns.set(board.columns);
    this.tasks.set(board.tasks);
    this.members.set(board.members);
    this.activity.set(activity);
    this.loading.set(false);
  }

  /** Subscribes to the server events (names match BoardEvents.cs). */
  private listen(): void {
    const on = <T>(name: string, handler: (payload: T) => void) => this.unsubscribers.push(this.hub.on(name, handler));

    on<Task>('TaskUpserted', (task) => this.upsertTasks([task]));
    on<Task[]>('TasksChanged', (tasks) => this.upsertTasks(tasks));
    on<number>('TaskDeleted', (id) => this.tasks.update((list) => list.filter((t) => t.id !== id)));
    on<Column[]>('ColumnsChanged', (columns) => {
      this.columns.set(columns);
      const ids = new Set(columns.map((c) => c.id));
      this.tasks.update((list) => list.filter((t) => ids.has(t.columnId))); // tasks of deleted columns
    });
    on<string>('BoardRenamed', (name) => this.board.update((b) => (b ? { ...b, name } : b)));
    on<number>('BoardDeleted', () => this.leaveWithMessage('This board was deleted.'));
    on<Member[]>('MembersChanged', (members) => {
      if (!members.some((m) => m.userId === this.auth.user()?.id)) {
        this.leaveWithMessage('You are no longer a member of this board.');
        return;
      }
      this.members.set(members);
    });
    on<Activity>('ActivityAdded', (entry) =>
      this.activity.update((list) => [entry, ...list.filter((a) => a.id !== entry.id)].slice(0, 50)),
    );
    on<OnlineUser[]>('OnlineUsers', (users) => this.online.set(users));

    this.unsubscribers.push(
      this.hub.onReconnected(async () => {
        await this.hub.joinBoard(this.boardId).catch(() => {});
        await this.reload().catch(() => {});
      }),
    );
  }

  private upsertTasks(changed: Task[]): void {
    const byId = new Map(changed.map((t) => [t.id, t]));
    this.tasks.update((list) => [...list.filter((t) => !byId.has(t.id)), ...changed]);
  }

  private left = false;

  private leaveWithMessage(message: string): void {
    if (this.left) return; // e.g. our own "leave" call and the MembersChanged event both arrive
    this.left = true;
    this.toast.info(message);
    this.router.navigateByUrl('/boards');
  }

  // ---------- Tasks ----------

  /**
   * Drag and drop. We move the card on screen right away (optimistic update) so it feels
   * instant, then tell the server. The server's TasksChanged broadcast confirms the final order.
   * If the server refuses, we reload the board to undo the local change.
   */
  moveTask(taskId: number, toColumnId: number, toIndex: number): void {
    const all = this.tasks();
    const task = all.find((t) => t.id === taskId);
    if (!task) return;

    const source = all.filter((t) => t.columnId === task.columnId && t.id !== taskId).sort(byPosition);
    const target = toColumnId === task.columnId ? source : all.filter((t) => t.columnId === toColumnId).sort(byPosition);
    target.splice(toIndex, 0, { ...task, columnId: toColumnId });

    const renumbered = new Map<number, Task>();
    source.forEach((t, i) => renumbered.set(t.id, { ...t, position: i }));
    target.forEach((t, i) => renumbered.set(t.id, { ...t, position: i }));
    this.tasks.set(all.map((t) => renumbered.get(t.id) ?? t));

    this.api.moveTask(taskId, toColumnId, toIndex).subscribe({
      next: (tasks) => this.upsertTasks(tasks),
      error: (err) => this.failAndReload(err),
    });
  }

  createTask(columnId: number, input: TaskInput): Promise<SaveResult> {
    return this.save(this.api.createTask(columnId, input));
  }

  updateTask(taskId: number, input: TaskInput, rowVersion: string): Promise<SaveResult> {
    return this.save(this.api.updateTask(taskId, input, rowVersion));
  }

  deleteTask(taskId: number): void {
    this.api.deleteTask(taskId).subscribe({
      next: () => this.tasks.update((list) => list.filter((t) => t.id !== taskId)),
      error: (err) => this.toast.error(errorMessage(err)),
    });
  }

  private async save(request: Observable<Task>): Promise<SaveResult> {
    try {
      const task = await firstValueFrom(request);
      this.upsertTasks([task]);
      return { ok: true };
    } catch (err) {
      const conflict = err instanceof HttpErrorResponse && err.status === 409 ? (err.error?.current as Task) : null;
      if (conflict) this.upsertTasks([conflict]);
      return { ok: false, conflict, message: errorMessage(err) };
    }
  }

  // ---------- Columns ----------

  addColumn(name: string): void {
    this.api.addColumn(this.boardId, name).subscribe({ error: (err) => this.toast.error(errorMessage(err)) });
  }

  renameColumn(columnId: number, name: string): void {
    this.api.renameColumn(columnId, name).subscribe({ error: (err) => this.failAndReload(err) });
  }

  deleteColumn(columnId: number): void {
    this.api.deleteColumn(columnId).subscribe({ error: (err) => this.toast.error(errorMessage(err)) });
  }

  /** Optimistic, like moveTask. */
  moveColumn(fromIndex: number, toIndex: number): void {
    const ordered = [...this.sortedColumns()];
    const [moved] = ordered.splice(fromIndex, 1);
    ordered.splice(toIndex, 0, moved);
    this.columns.set(ordered.map((c, i) => ({ ...c, position: i })));

    this.api.reorderColumns(this.boardId, ordered.map((c) => c.id)).subscribe({ error: (err) => this.failAndReload(err) });
  }

  // ---------- Board & members ----------

  renameBoard(name: string): void {
    this.api.renameBoard(this.boardId, name).subscribe({ error: (err) => this.failAndReload(err) });
  }

  invite(email: string): Promise<string | null> {
    return firstValueFrom(this.api.invite(this.boardId, email)).then(
      (members) => {
        this.members.set(members);
        return null;
      },
      (err) => errorMessage(err),
    );
  }

  removeMember(userId: string): void {
    this.api.removeMember(this.boardId, userId).subscribe({ error: (err) => this.toast.error(errorMessage(err)) });
  }

  leaveBoard(): void {
    const me = this.auth.user();
    if (!me) return;
    this.api.removeMember(this.boardId, me.id).subscribe({
      next: () => this.leaveWithMessage('You left the board.'),
      error: (err) => this.toast.error(errorMessage(err)),
    });
  }

  private failAndReload(err: unknown): void {
    this.toast.error(errorMessage(err));
    this.reload().catch(() => {});
  }
}
