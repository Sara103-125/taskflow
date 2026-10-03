import { Component, OnInit, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Priority, Task, TaskInput } from '../../core/models';
import { BoardStore } from './board.store';

/**
 * Create / edit a task.
 *
 * Concurrency: we remember the version of the task the form was loaded from ("base").
 * If a newer version shows up (a live update from a teammate, or a 409 from the server),
 * we show a banner and let the user choose: load their version, or keep my changes.
 */
@Component({
  selector: 'app-task-dialog',
  imports: [FormsModule],
  templateUrl: './task-dialog.html',
})
export class TaskDialog implements OnInit {
  protected readonly store = inject(BoardStore);

  /** null = create a new task in columnId. */
  readonly taskId = input<number | null>(null);
  readonly columnId = input.required<number>();
  readonly closed = output<void>();

  protected readonly priorities: Priority[] = ['Low', 'Medium', 'High'];
  protected form: TaskInput = { title: '', description: null, assigneeId: null, dueDate: null, priority: 'Medium' };
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  /** The version of the task the form is based on. */
  private readonly base = signal<Task | null>(null);
  /** The newest version we know about (kept up to date by SignalR). */
  protected readonly latest = computed(() => this.store.tasks().find((t) => t.id === this.taskId()) ?? null);

  protected readonly isEdit = computed(() => this.taskId() !== null);
  protected readonly wasDeleted = computed(() => this.isEdit() && this.base() !== null && this.latest() === null);
  protected readonly changedByOthers = computed(() => {
    const latest = this.latest();
    const base = this.base();
    return !!latest && !!base && latest.rowVersion !== base.rowVersion;
  });

  constructor() {
    // If someone only moved the card (content unchanged), quietly accept the new version.
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

  protected remove(): void {
    const base = this.base();
    if (base && confirm(`Delete "${base.title}"?`)) {
      this.store.deleteTask(base.id);
      this.closed.emit();
    }
  }

  private loadFrom(task: Task): void {
    this.base.set(task);
    this.form = {
      title: task.title,
      description: task.description,
      assigneeId: task.assigneeId,
      dueDate: task.dueDate,
      priority: task.priority,
    };
  }
}

function sameContent(a: Task, b: Task): boolean {
  return (
    a.title === b.title &&
    a.description === b.description &&
    a.assigneeId === b.assigneeId &&
    a.dueDate === b.dueDate &&
    a.priority === b.priority
  );
}
