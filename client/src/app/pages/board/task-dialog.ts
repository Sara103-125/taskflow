import { Component, OnDestroy, OnInit, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../core/auth.service';
import { Priority, Task, TaskInput } from '../../core/models';
import { TimeAgoPipe } from '../../shared/format';
import { BoardStore } from './board.store';

type Tab = 'details' | 'checklist' | 'comments' | 'history';

/**
 * Create / edit a task.
 *
 * Concurrency: we remember the version of the task the form was loaded from ("base").
 * If a newer version shows up (a live update from a teammate, or a 409 from the server),
 * we show a banner and let the user choose: load their version, or keep my changes.
 *
 * Checklist and comments save immediately (they have their own tables, so they never
 * conflict with the details form).
 */
@Component({
  selector: 'app-task-dialog',
  imports: [FormsModule, TimeAgoPipe],
  templateUrl: './task-dialog.html',
})
export class TaskDialog implements OnInit, OnDestroy {
  protected readonly store = inject(BoardStore);
  private readonly auth = inject(AuthService);

  /** null = create a new task in columnId. */
  readonly taskId = input<number | null>(null);
  readonly columnId = input.required<number>();
  readonly closed = output<void>();

  protected readonly priorities: Priority[] = ['Low', 'Medium', 'High'];
  protected readonly tab = signal<Tab>('details');
  protected form: TaskInput = { title: '', description: null, assigneeId: null, dueDate: null, priority: 'Medium', labelIds: [] };
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected newChecklistText = '';
  protected commentText = signal('');

  /** The version of the task the form is based on. */
  private readonly base = signal<Task | null>(null);
  /** The newest version we know about (kept up to date by SignalR). */
  protected readonly latest = computed(() => this.store.tasks().find((t) => t.id === this.taskId()) ?? null);

  protected readonly isEdit = computed(() => this.taskId() !== null);
  protected readonly details = this.store.openDetails;
  protected readonly myId = computed(() => this.auth.user()?.id);
  protected readonly wasDeleted = computed(() => this.isEdit() && this.base() !== null && this.latest() === null);
  protected readonly changedByOthers = computed(() => {
    const latest = this.latest();
    const base = this.base();
    return !!latest && !!base && latest.rowVersion !== base.rowVersion;
  });
  protected readonly othersHere = computed(() => this.store.editingByTask().get(this.taskId() ?? -1) ?? []);

  /** Members whose name starts with what was typed after the last "@", e.g. "@Sa" -> Sara. */
  protected readonly mentionSuggestions = computed(() => {
    const match = /@([^@\n]*)$/.exec(this.commentText());
    if (!match) return [];
    const typed = match[1].toLowerCase();
    return this.store
      .members()
      .filter((m) => m.displayName.toLowerCase().startsWith(typed) && m.displayName.toLowerCase() !== typed)
      .slice(0, 5);
  });

  constructor() {
    // If someone only moved the card or ticked a checklist item (content unchanged), quietly
    // accept the new version instead of showing the conflict banner.
    effect(() => {
      const latest = this.latest();
      const base = this.base();
      if (latest && base && latest.rowVersion !== base.rowVersion && sameContent(latest, base)) {
        this.base.set(latest);
      }
    });
  }

  ngOnInit(): void {
    const task = this.latest();
    if (task) this.loadFrom(task);
    if (this.isEdit()) this.store.openTask(this.taskId());
  }

  ngOnDestroy(): void {
    if (this.isEdit()) this.store.openTask(null);
  }

  // ---------- Details ----------

  protected toggleLabel(id: number): void {
    const ids = this.form.labelIds;
    this.form.labelIds = ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id];
  }

  protected async save(): Promise<void> {
    if (!this.form.title.trim()) return;
    this.saving.set(true);
    this.error.set(null);

    const input: TaskInput = {
      ...this.form,
      title: this.form.title.trim(),
      description: this.form.description?.trim() || null,
      dueDate: this.form.dueDate || null,
    };
    const base = this.base();
    const result = base
      ? await this.store.updateTask(base.id, input, base.rowVersion)
      : await this.store.createTask(this.columnId(), input);

    this.saving.set(false);
    if (result.ok) {
      this.closed.emit();
    } else {
      // On a 409 the store has already stored the latest version, so the banner appears.
      this.error.set(result.message);
    }
  }

  /** Throw away my edits and show what the other person saved. */
  protected loadTheirs(): void {
    const latest = this.latest();
    if (latest) this.loadFrom(latest);
    this.error.set(null);
  }

  /** Keep my edits; the next Save will knowingly replace their version. */
  protected keepMine(): void {
    this.base.set(this.latest());
    this.error.set(null);
  }

  /** Soft delete: the task can be restored from the board's archive. */
  protected archive(): void {
    const base = this.base();
    if (base) {
      this.store.archiveTask(base.id);
      this.closed.emit();
    }
  }

  // ---------- Checklist ----------

  protected addChecklistItem(): void {
    const text = this.newChecklistText.trim();
    const id = this.taskId();
    if (!text || id === null) return;
    this.store.addChecklistItem(id, text);
    this.newChecklistText = '';
  }

  // ---------- Comments ----------

  protected insertMention(name: string): void {
    this.commentText.update((text) => text.replace(/@([^@\n]*)$/, `@${name} `));
  }

  protected async addComment(): Promise<void> {
    const body = this.commentText().trim();
    const id = this.taskId();
    if (!body || id === null) return;
    if (await this.store.addComment(id, body)) this.commentText.set('');
  }

  protected canDeleteComment(authorId: string): boolean {
    return authorId === this.myId() || this.store.isOwner();
  }

  /** Splits a comment into plain text and "@Name" parts, so mentions can be highlighted. */
  protected segments(body: string): Array<{ text: string; mention: boolean }> {
    const names = this.store.members().map((m) => m.displayName).sort((a, b) => b.length - a.length);
    if (names.length === 0) return [{ text: body, mention: false }];
    const escaped = names.map((n) => n.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
    // split() with a capture group returns [text, mention, text, mention, ...]
    const pattern = new RegExp(`(@(?:${escaped.join('|')}))(?![\\p{L}\\p{N}_])`, 'iu');
    return body
      .split(pattern)
      .map((text, i) => ({ text, mention: i % 2 === 1 }))
      .filter((part) => part.text !== '');
  }

  private loadFrom(task: Task): void {
    this.base.set(task);
    this.form = {
      title: task.title,
      description: task.description,
      assigneeId: task.assigneeId,
      dueDate: task.dueDate,
      priority: task.priority,
      labelIds: [...task.labelIds],
    };
  }
}

function sameContent(a: Task, b: Task): boolean {
  return (
    a.title === b.title &&
    a.description === b.description &&
    a.assigneeId === b.assigneeId &&
    a.dueDate === b.dueDate &&
    a.priority === b.priority &&
    [...a.labelIds].sort().join() === [...b.labelIds].sort().join()
  );
}
