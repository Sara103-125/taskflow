import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { errorMessage } from '../../core/errors';
import { MyTask } from '../../core/models';
import { dueLabel, isOverdue } from '../../shared/format';
import { ToastService } from '../../shared/toast.service';

/** Every task assigned to me on all my boards, soonest due first. */
@Component({
  selector: 'app-my-tasks',
  imports: [RouterLink],
  template: `
    <section class="page">
      <div class="page-header">
        <h1>My tasks</h1>
        <label class="check">
          <input type="checkbox" [checked]="hideDone()" (change)="hideDone.set(!hideDone())" />
          Hide tasks in "Done"
        </label>
      </div>

      @if (visible(); as list) {
        <div class="card table-card">
          <table class="task-table">
            <thead>
              <tr>
                <th>Task</th>
                <th>Board</th>
                <th>Status</th>
                <th>Priority</th>
                <th>Due</th>
              </tr>
            </thead>
            <tbody>
              @for (t of list; track t.id) {
                <tr>
                  <td>
                    <a class="link" [routerLink]="['/boards', t.boardId]" [queryParams]="{ task: t.id }">{{ t.title }}</a>
                    @if (t.checklistTotal) {
                      <span class="meta-pill" [class.complete]="t.checklistDone === t.checklistTotal">&#10003; {{ t.checklistDone }}/{{ t.checklistTotal }}</span>
                    }
                  </td>
                  <td class="muted">{{ t.boardName }}</td>
                  <td><span class="badge">{{ t.columnName }}</span></td>
                  <td><span class="priority priority-{{ t.priority.toLowerCase() }}">{{ t.priority }}</span></td>
                  <td [class.overdue]="isOverdue(t.dueDate) && t.columnName !== 'Done'">{{ t.dueDate ? dueLabel(t.dueDate) : '—' }}</td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="5" class="empty">Nothing assigned to you. Enjoy the calm!</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      } @else {
        <p class="muted">Loading...</p>
      }
    </section>
  `,
})
export class MyTasksPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  protected readonly tasks = signal<MyTask[] | null>(null);
  protected readonly hideDone = signal(true);
  protected readonly visible = computed(() => {
    const list = this.tasks();
    return list && (this.hideDone() ? list.filter((t) => t.columnName !== 'Done') : list);
  });
  protected readonly isOverdue = isOverdue;
  protected readonly dueLabel = dueLabel;

  ngOnInit(): void {
    this.api.getMyTasks().subscribe({
      next: (tasks) => this.tasks.set(tasks),
      error: (err) => this.toast.error(errorMessage(err)),
    });
  }
}
