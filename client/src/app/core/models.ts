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
}

export interface BoardDetail {
  id: number;
  name: string;
  isDemo: boolean;
  myRole: BoardRole;
  members: Member[];
  columns: Column[];
  tasks: Task[];
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
}
