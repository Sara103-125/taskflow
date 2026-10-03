import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { BoardHubService } from '../../core/board-hub.service';
import { errorMessage } from '../../core/errors';
import {
  Activity,
  BoardDetail,
  ChecklistItem,
  Column,
  Comment,
  Editor,
  Label,
  Member,
  OnlineUser,
  Priority,
  Task,
  TaskDetails,
  TaskInput,
} from '../../core/models';
import { isOverdue, localDateText } from '../../shared/format';
import { ToastService } from '../../shared/toast.service';

/** Result of saving a task: either it worked, or someone else changed it first (409). */
export type SaveResult = { ok: true } | { ok: false; conflict: Task | null; message: string };

export interface TaskFilter {
  text: string;
  assigneeId: string; // '' = anyone, 'me', 'none' or a user id
  priority: Priority | '';
  labelId: number | null;
  due: '' | 'overdue' | 'week';
}

export const EMPTY_FILTER: TaskFilter = { text: '', assigneeId: '', priority: '', labelId: null, due: '' };

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
  private left = false;

  readonly board = signal<BoardDetail | null>(null);
  readonly columns = signal<Column[]>([]);
  readonly tasks = signal<Task[]>([]);
  readonly members = signal<Member[]>([]);
  readonly labels = signal<Label[]>([]);
  readonly activity = signal<Activity[]>([]);
  readonly online = signal<OnlineUser[]>([]);
  readonly editors = signal<Editor[]>([]);
  readonly filter = signal<TaskFilter>(EMPTY_FILTER);
  readonly liveStatus = this.hub.status;

  /** The task whose dialog is open, and its checklist / comments / history (kept live). */
  readonly openTaskId = signal<number | null>(null);
  readonly openDetails = signal<TaskDetails | null>(null);

  readonly isOwner = computed(() => this.board()?.myRole === 'Owner');
  readonly sortedColumns = computed(() => [...this.columns()].sort(byPosition));
  readonly labelsById = computed(() => new Map(this.labels().map((l) => [l.id, l])));
  readonly isFiltering = computed(() => JSON.stringify(this.filter()) !== JSON.stringify(EMPTY_FILTER));

  /** Visible tasks (after filters) grouped by column and sorted, e.g. { 12: [taskA, taskB], 13: [] }. */
  readonly tasksByColumn = computed(() => {
    const matches = this.matcher();
    const groups: Record<number, Task[]> = {};
    for (const column of this.columns()) groups[column.id] = [];
    for (const task of this.tasks()) if (matches(task)) groups[task.columnId]?.push(task);
    for (const id in groups) groups[id].sort(byPosition);
    return groups;
  });

  /** taskId -> names of *other* people who have that task open. */
  readonly editingByTask = computed(() => {
    const me = this.auth.user()?.id;
    const map = new Map<number, string[]>();
    for (const e of this.editors()) {
      if (e.userId === me) continue;
      map.set(e.taskId, [...(map.get(e.taskId) ?? []), e.displayName]);
    }
    return map;
  });

  // ---------- Loading & live connection ----------

  async open(boardId: number): Promise<void> {
    this.boardId = boardId;
    try {
      this.listen(); // listen before joining so we don't miss the first "OnlineUsers"
      await this.reload();
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
    this.labels.set(board.labels);
    this.activity.set(activity);
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
    on<Label[]>('LabelsChanged', (labels) => this.labels.set(labels));
    on<string>('BoardRenamed', (name) => this.board.update((b) => (b ? { ...b, name } : b)));
    on<number>('BoardDeleted', () => this.leaveWithMessage('This board was deleted.'));
    on<number>('BoardArchived', () => this.leaveWithMessage('This board was archived by its owner.'));
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
    on<Editor[]>('EditorsChanged', (editors) => this.editors.set(editors));

    // Live details for the open task dialog.
    on<{ taskId: number; items: ChecklistItem[] }>('ChecklistChanged', ({ taskId, items }) =>
      this.updateOpenDetails(taskId, (d) => ({ ...d, checklist: items })),
    );
    on<Comment>('CommentAdded', (comment) =>
      this.updateOpenDetails(comment.taskId, (d) => ({
        ...d,
        comments: [...d.comments.filter((c) => c.id !== comment.id), comment],
      })),
    );
    on<{ taskId: number; commentId: number }>('CommentDeleted', ({ taskId, commentId }) =>
      this.updateOpenDetails(taskId, (d) => ({ ...d, comments: d.comments.filter((c) => c.id !== commentId) })),
    );

    this.unsubscribers.push(
      this.hub.onReconnected(async () => {
        await this.hub.joinBoard(this.boardId).catch(() => {});
        await this.reload().catch(() => {});
        const open = this.openTaskId();
        if (open !== null) this.hub.setEditing(this.boardId, open);
      }),
    );
  }

  private upsertTasks(changed: Task[]): void {
    const byId = new Map(changed.map((t) => [t.id, t]));
    this.tasks.update((list) => [...list.filter((t) => !byId.has(t.id)), ...changed]);
    // The open task changed: its history has a new line, so refresh the details.
    const open = this.openTaskId();
    if (open !== null && byId.has(open)) this.refreshOpenDetails();
  }

  private leaveWithMessage(message: string): void {
    if (this.left) return; // e.g. our own "leave" call and the MembersChanged event both arrive
    this.left = true;
    this.toast.info(message);
    this.router.navigateByUrl('/boards');
  }

  // ---------- Filters ----------

  setFilter(patch: Partial<TaskFilter>): void {
    this.filter.update((f) => ({ ...f, ...patch }));
  }

  clearFilter(): void {
    this.filter.set(EMPTY_FILTER);
  }

  /** Builds a "does this task match the filters?" function from the current filter. */
  private matcher(): (task: Task) => boolean {
    const f = this.filter();
    const text = f.text.trim().toLowerCase();
    const me = this.auth.user()?.id;
    const weekAhead = new Date();
    weekAhead.setDate(weekAhead.getDate() + 7);
    const weekText = localDateText(weekAhead);

    return (t) => {
      if (text && !`${t.title} ${t.description ?? ''}`.toLowerCase().includes(text)) return false;
      if (f.assigneeId === 'me' && t.assigneeId !== me) return false;
      if (f.assigneeId === 'none' && t.assigneeId !== null) return false;
      if (f.assigneeId && !['me', 'none'].includes(f.assigneeId) && t.assigneeId !== f.assigneeId) return false;
      if (f.priority && t.priority !== f.priority) return false;
      if (f.labelId !== null && !t.labelIds.includes(f.labelId)) return false;
      if (f.due === 'overdue' && !isOverdue(t.dueDate)) return false;
      if (f.due === 'week' && (!t.dueDate || t.dueDate > weekText)) return false;
      return true;
    };
  }

  // ---------- Tasks ----------

  /**
   * Drag and drop. We move the card on screen right away (optimistic update) so it feels
   * instant, then tell the server. The server's TasksChanged broadcast confirms the final order.
   * If the server refuses, we reload the board to undo the local change.
   *
   * While filters hide some cards, the drop index is an index into the *visible* list,
   * so we translate it into a position in the full column first.
   */
  moveTask(taskId: number, toColumnId: number, visibleIndex: number): void {
    const all = this.tasks();
    const task = all.find((t) => t.id === taskId);
    if (!task) return;

    const source = all.filter((t) => t.columnId === task.columnId && t.id !== taskId).sort(byPosition);
    const target = toColumnId === task.columnId ? source : all.filter((t) => t.columnId === toColumnId).sort(byPosition);
    const toIndex = this.toFullIndex(target, toColumnId, taskId, visibleIndex);
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

  private toFullIndex(fullTarget: Task[], columnId: number, taskId: number, visibleIndex: number): number {
    const visible = (this.tasksByColumn()[columnId] ?? []).filter((t) => t.id !== taskId);
    if (visible.length === 0) return fullTarget.length;
    if (visibleIndex >= visible.length) return fullTarget.findIndex((t) => t.id === visible[visible.length - 1].id) + 1;
    return fullTarget.findIndex((t) => t.id === visible[visibleIndex].id);
  }

  createTask(columnId: number, input: TaskInput): Promise<SaveResult> {
    return this.save(this.api.createTask(columnId, input));
  }

  updateTask(taskId: number, input: TaskInput, rowVersion: string): Promise<SaveResult> {
    return this.save(this.api.updateTask(taskId, input, rowVersion));
  }

  archiveTask(taskId: number): void {
    this.api.archiveTask(taskId).subscribe({
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

  // ---------- Open task: details, checklist, comments ----------

  /** Called by the task dialog. Loads details and tells teammates we're looking at the task. */
  openTask(taskId: number | null): void {
    this.openTaskId.set(taskId);
    this.openDetails.set(null);
    this.hub.setEditing(this.boardId, taskId);
    if (taskId !== null) this.refreshOpenDetails();
  }

  private refreshOpenDetails(): void {
    const taskId = this.openTaskId();
    if (taskId === null) return;
    this.api.getTaskDetails(taskId).subscribe({
      next: (details) => {
        if (this.openTaskId() === taskId) this.openDetails.set(details);
      },
      error: () => {},
    });
  }

  private updateOpenDetails(taskId: number, change: (d: TaskDetails) => TaskDetails): void {
    const details = this.openDetails();
    if (this.openTaskId() === taskId && details) this.openDetails.set(change(details));
  }

  addChecklistItem(taskId: number, text: string): void {
    this.api.addChecklistItem(taskId, text).subscribe({ error: (err) => this.toast.error(errorMessage(err)) });
  }

  toggleChecklistItem(item: ChecklistItem): void {
    this.api.updateChecklistItem(item.id, item.text, !item.isDone).subscribe({ error: (err) => this.toast.error(errorMessage(err)) });
  }

  deleteChecklistItem(id: number): void {
    this.api.deleteChecklistItem(id).subscribe({ error: (err) => this.toast.error(errorMessage(err)) });
  }

  addComment(taskId: number, body: string): Promise<boolean> {
    return firstValueFrom(this.api.addComment(taskId, body)).then(
      () => true,
      (err) => {
        this.toast.error(errorMessage(err));
        return false;
      },
    );
  }

  deleteComment(id: number): void {
    this.api.deleteComment(id).subscribe({ error: (err) => this.toast.error(errorMessage(err)) });
  }

  // ---------- Labels ----------

  addLabel(name: string, color: string): Promise<string | null> {
    return firstValueFrom(this.api.addLabel(this.boardId, name, color)).then(
      () => null,
      (err) => errorMessage(err),
    );
  }

  updateLabel(id: number, name: string, color: string): void {
    this.api.updateLabel(id, name, color).subscribe({ error: (err) => this.toast.error(errorMessage(err)) });
  }

  deleteLabel(id: number): void {
    this.api.deleteLabel(id).subscribe({ error: (err) => this.toast.error(errorMessage(err)) });
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

  get id(): number {
    return this.boardId;
  }

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
