export enum LogLevel {
  Info = 0,
  Warning = 1,
  Error = 2,
  Critical = 3
}

export interface LogEntry {
  id: string;
  serviceName: string;
  level: LogLevel;
  message: string;
  stackTrace?: string;
  createdAtUtc: string;
  errorGroupId?: string;
}

export interface ErrorGroup {
  id: string;
  errorClass: string;
  summary: string;
  suggestedPatch: string;
  firstSeenUtc: string;
  lastSeenUtc: string;
  count: number;
}
