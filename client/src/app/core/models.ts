// These interfaces mirror the C# DTOs in server/TaskFlow.Api/Dtos.

export type BoardRole = 'Owner' | 'Member';
export type Priority = 'Low' | 'Medium' | 'High';

export interface User {
  id: string;
  email: string;
  displayName: string;
}

export interface AuthResponse {
  token: string;
  user: User;
}

export interface BoardSummary {
  id: number;
  name: string;
  myRole: BoardRole;
  memberCount: number;
  isDemo: boolean;
  archivedAt: string | null;
}

export interface Member {
  userId: string;
  displayName: string;
  email: string;
  role: BoardRole;
}

export interface Column {
  id: number;
  name: string;
  position: number;
}

export interface Task {
  id: number;
  columnId: number;
  title: string;
  description: string | null;
  assigneeId: string | null;
  assigneeName: string | null;
  dueDate: string | null; // "2026-10-20"
  priority: Priority;
  position: number;
  rowVersion: string; // base64, sent back on update for concurrency checks
  labelIds: number[];
  checklistDone: number;
  checklistTotal: number;
  commentCount: number;
}

export interface BoardDetail {
  id: number;
  name: string;
  isDemo: boolean;
  myRole: BoardRole;
  members: Member[];
  columns: Column[];
  tasks: Task[];
  labels: Label[];
}

export interface Activity {
  id: number;
  message: string;
  createdAt: string;
}

export interface OnlineUser {
  userId: string;
  displayName: string;
}

export interface TaskInput {
  title: string;
  description: string | null;
  assigneeId: string | null;
  dueDate: string | null;
  priority: Priority;
  labelIds: number[];
}

export interface Label {
  id: number;
  name: string;
  color: string;
}

export interface ChecklistItem {
  id: number;
  text: string;
  isDone: boolean;
  position: number;
}

export interface Comment {
  id: number;
  taskId: number;
  authorId: string;
  authorName: string;
  body: string;
  createdAt: string;
}

export interface HistoryEntry {
  id: number;
  userName: string;
  change: string;
  createdAt: string;
}

export interface TaskDetails {
  checklist: ChecklistItem[];
  comments: Comment[];
  history: HistoryEntry[];
}

export interface ArchivedTask {
  id: number;
  title: string;
  columnName: string;
  archivedAt: string;
}

export interface MyTask {
  id: number;
  title: string;
  boardId: number;
  boardName: string;
  columnName: string;
  dueDate: string | null;
  priority: Priority;
  checklistDone: number;
  checklistTotal: number;
}

export interface Notification {
  id: number;
  boardId: number;
  taskId: number | null;
  message: string;
  isRead: boolean;
  createdAt: string;
}

/** "displayName has task taskId open right now." */
export interface Editor {
  taskId: number;
  userId: string;
  displayName: string;
}
