import { Component, OnInit, inject, output, signal } from '@angular/core';
import { ApiService } from '../../core/api.service';
import { errorMessage } from '../../core/errors';
import { ArchivedTask } from '../../core/models';
import { TimeAgoPipe } from '../../shared/format';
import { ToastService } from '../../shared/toast.service';
import { BoardStore } from './board.store';

/** Archived tasks of this board: restore them, or delete them for good. */
@Component({
  selector: 'app-archive-dialog',
  imports: [TimeAgoPipe],
  template: `
    <div class="backdrop" (click)="closed.emit()">
      <div class="card dialog" role="dialog" aria-modal="true" (click)="$event.stopPropagation()">
        <h2>Archived tasks</h2>

        @if (tasks(); as list) {
          <ul class="member-list">
            @for (task of list; track task.id) {
              <li>
                <div>
                  <strong>{{ task.title }}</strong>
                  <div class="muted small">from {{ task.columnName }} &middot; archived {{ task.archivedAt | timeAgo }}</div>
                </div>
                <button class="btn btn-small" (click)="restore(task)">Restore</button>
                <button class="btn btn-ghost btn-small danger" (click)="deleteForever(task)">Delete forever</button>
              </li>
            } @empty {
              <li class="muted">Nothing archived. Archive a task from its dialog to hide it without losing it.</li>
            }
          </ul>
        } @else {
          <p class="muted">Loading...</p>
        }

        <div class="dialog-actions">
          <span class="spacer"></span>
          <button class="btn" (click)="closed.emit()">Close</button>
        </div>
      </div>
    </div>
  `,
})
export class ArchiveDialog implements OnInit {
  private readonly store = inject(BoardStore);
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  readonly closed = output<void>();

  protected readonly tasks = signal<ArchivedTask[] | null>(null);

  ngOnInit(): void {
    this.api.getArchivedTasks(this.store.id).subscribe({
      next: (tasks) => this.tasks.set(tasks),
      error: (err) => this.toast.error(errorMessage(err)),
    });
  }

  protected restore(task: ArchivedTask): void {
    // The server broadcasts TaskUpserted, so the card reappears on every screen.
    this.api.restoreTask(task.id).subscribe({
      next: () => this.removeFromList(task.id),
      error: (err) => this.toast.error(errorMessage(err)),
    });
  }

  protected deleteForever(task: ArchivedTask): void {
    if (!confirm(`Permanently delete "${task.title}"? This can't be undone.`)) return;
    this.api.deleteTask(task.id).subscribe({
      next: () => this.removeFromList(task.id),
      error: (err) => this.toast.error(errorMessage(err)),
    });
  }

  private removeFromList(id: number): void {
    this.tasks.update((list) => (list ?? []).filter((t) => t.id !== id));
  }
}
