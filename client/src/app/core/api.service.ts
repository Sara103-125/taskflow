import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  Activity,
  ArchivedTask,
  BoardDetail,
  BoardSummary,
  ChecklistItem,
  Column,
  Comment,
  Label,
  Member,
  MyTask,
  Notification,
  Task,
  TaskDetails,
  TaskInput,
} from './models';

/** One method per API endpoint. No state here — see BoardStore for that. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly api = `${environment.apiUrl}/api`;

  // Boards
  getBoards(): Observable<BoardSummary[]> {
    return this.http.get<BoardSummary[]>(`${this.api}/boards`);
  }
  getBoard(id: number): Observable<BoardDetail> {
    return this.http.get<BoardDetail>(`${this.api}/boards/${id}`);
  }
  createBoard(name: string): Observable<BoardSummary> {
    return this.http.post<BoardSummary>(`${this.api}/boards`, { name });
  }
  renameBoard(id: number, name: string): Observable<void> {
    return this.http.put<void>(`${this.api}/boards/${id}`, { name });
  }
  deleteBoard(id: number): Observable<void> {
    return this.http.delete<void>(`${this.api}/boards/${id}`);
  }
  archiveBoard(id: number): Observable<void> {
    return this.http.post<void>(`${this.api}/boards/${id}/archive`, {});
  }
  restoreBoard(id: number): Observable<void> {
    return this.http.post<void>(`${this.api}/boards/${id}/restore`, {});
  }
  invite(boardId: number, email: string): Observable<Member[]> {
    return this.http.post<Member[]>(`${this.api}/boards/${boardId}/members`, { email });
  }
  removeMember(boardId: number, userId: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/boards/${boardId}/members/${userId}`);
  }
  getActivity(boardId: number, take = 30): Observable<Activity[]> {
    return this.http.get<Activity[]>(`${this.api}/boards/${boardId}/activity`, { params: { take } });
  }

  // Labels
  addLabel(boardId: number, name: string, color: string): Observable<Label> {
    return this.http.post<Label>(`${this.api}/boards/${boardId}/labels`, { name, color });
  }
  updateLabel(id: number, name: string, color: string): Observable<void> {
    return this.http.put<void>(`${this.api}/labels/${id}`, { name, color });
  }
  deleteLabel(id: number): Observable<void> {
    return this.http.delete<void>(`${this.api}/labels/${id}`);
  }

  // Columns
  addColumn(boardId: number, name: string): Observable<Column> {
    return this.http.post<Column>(`${this.api}/boards/${boardId}/columns`, { name });
  }
  reorderColumns(boardId: number, columnIds: number[]): Observable<void> {
    return this.http.put<void>(`${this.api}/boards/${boardId}/columns/order`, { columnIds });
  }
  renameColumn(id: number, name: string): Observable<void> {
    return this.http.put<void>(`${this.api}/columns/${id}`, { name });
  }
  deleteColumn(id: number): Observable<void> {
    return this.http.delete<void>(`${this.api}/columns/${id}`);
  }

  // Tasks
  createTask(columnId: number, input: TaskInput): Observable<Task> {
    return this.http.post<Task>(`${this.api}/tasks`, { columnId, ...input });
  }
  updateTask(id: number, input: TaskInput, rowVersion: string): Observable<Task> {
    return this.http.put<Task>(`${this.api}/tasks/${id}`, { ...input, rowVersion });
  }
  moveTask(id: number, columnId: number, position: number): Observable<Task[]> {
    return this.http.put<Task[]>(`${this.api}/tasks/${id}/move`, { columnId, position });
  }
  /** Permanent delete (offered from the archive). */
  deleteTask(id: number): Observable<void> {
    return this.http.delete<void>(`${this.api}/tasks/${id}`);
  }
  archiveTask(id: number): Observable<void> {
    return this.http.post<void>(`${this.api}/tasks/${id}/archive`, {});
  }
  restoreTask(id: number): Observable<Task> {
    return this.http.post<Task>(`${this.api}/tasks/${id}/restore`, {});
  }
  getArchivedTasks(boardId: number): Observable<ArchivedTask[]> {
    return this.http.get<ArchivedTask[]>(`${this.api}/boards/${boardId}/archived-tasks`);
  }
  getTaskDetails(id: number): Observable<TaskDetails> {
    return this.http.get<TaskDetails>(`${this.api}/tasks/${id}/details`);
  }
  getMyTasks(): Observable<MyTask[]> {
    return this.http.get<MyTask[]>(`${this.api}/tasks/mine`);
  }

  // Checklist
  addChecklistItem(taskId: number, text: string): Observable<ChecklistItem> {
    return this.http.post<ChecklistItem>(`${this.api}/tasks/${taskId}/checklist`, { text });
  }
  updateChecklistItem(id: number, text: string, isDone: boolean): Observable<void> {
    return this.http.put<void>(`${this.api}/checklist/${id}`, { text, isDone });
  }
  deleteChecklistItem(id: number): Observable<void> {
    return this.http.delete<void>(`${this.api}/checklist/${id}`);
  }

  // Comments
  addComment(taskId: number, body: string): Observable<Comment> {
    return this.http.post<Comment>(`${this.api}/tasks/${taskId}/comments`, { body });
  }
  deleteComment(id: number): Observable<void> {
    return this.http.delete<void>(`${this.api}/comments/${id}`);
  }

  // Notifications
  getNotifications(): Observable<{ unreadCount: number; items: Notification[] }> {
    return this.http.get<{ unreadCount: number; items: Notification[] }>(`${this.api}/notifications`);
  }
  markNotificationRead(id: number): Observable<void> {
    return this.http.post<void>(`${this.api}/notifications/${id}/read`, {});
  }
  markAllNotificationsRead(): Observable<void> {
    return this.http.post<void>(`${this.api}/notifications/read-all`, {});
  }
}
