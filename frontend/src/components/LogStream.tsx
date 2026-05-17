import React from 'react';
import { LogEntry, LogLevel } from '../types';
import { AlertCircle, Info, AlertTriangle, XCircle } from 'lucide-react';

interface LogStreamProps {
  logs: LogEntry[];
  filterService: string;
  setFilterService: (value: string) => void;
  filterLevel: LogLevel | null;
  setFilterLevel: (value: LogLevel | null) => void;
}

const LogStream: React.FC<LogStreamProps> = ({
  logs,
  filterService,
  setFilterService,
  filterLevel,
  setFilterLevel,
}) => {
  const getLevelIcon = (level: LogLevel) => {
    switch (level) {
      case LogLevel.Info:
        return <Info className="w-4 h-4 text-gray-400" />;
      case LogLevel.Warning:
        return <AlertTriangle className="w-4 h-4 text-yellow-400" />;
      case LogLevel.Error:
        return <XCircle className="w-4 h-4 text-red-500" />;
      case LogLevel.Critical:
        return <AlertCircle className="w-4 h-4 text-red-600" />;
    }
  };

  const getLevelColor = (level: LogLevel) => {
    switch (level) {
      case LogLevel.Info:
        return 'text-gray-300';
      case LogLevel.Warning:
        return 'text-yellow-300';
      case LogLevel.Error:
        return 'text-red-400';
      case LogLevel.Critical:
        return 'text-red-500 font-bold';
    }
  };

  const getLevelBadgeColor = (level: LogLevel) => {
    switch (level) {
      case LogLevel.Info:
        return 'bg-gray-700 text-gray-300';
      case LogLevel.Warning:
        return 'bg-yellow-900 text-yellow-300';
      case LogLevel.Error:
        return 'bg-red-900 text-red-300';
      case LogLevel.Critical:
        return 'bg-red-950 text-red-400';
    }
  };

  const formatTimestamp = (timestamp: string) => {
    const date = new Date(timestamp);
    return date.toLocaleTimeString('ru-RU', {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
  };

  return (
    <div className="flex flex-col h-full">
      <div className="bg-slate-800 border-b border-slate-700 p-4">
        <h2 className="text-xl font-bold text-white mb-4 flex items-center gap-2">
          <AlertCircle className="w-5 h-5" />
          Live Log Stream
        </h2>
        <div className="flex gap-4">
          <div className="flex-1">
            <label className="block text-sm text-gray-400 mb-1">Service Name</label>
            <input
              type="text"
              value={filterService}
              onChange={(e) => setFilterService(e.target.value)}
              placeholder="Filter by service..."
              className="w-full bg-slate-700 border border-slate-600 rounded px-3 py-2 text-white placeholder-gray-500 focus:outline-none focus:border-blue-500"
            />
          </div>
          <div className="w-48">
            <label className="block text-sm text-gray-400 mb-1">Log Level</label>
            <select
              value={filterLevel ?? ''}
              onChange={(e) => setFilterLevel(e.target.value ? Number(e.target.value) as LogLevel : null)}
              className="w-full bg-slate-700 border border-slate-600 rounded px-3 py-2 text-white focus:outline-none focus:border-blue-500"
            >
              <option value="">All Levels</option>
              <option value={LogLevel.Info}>Info</option>
              <option value={LogLevel.Warning}>Warning</option>
              <option value={LogLevel.Error}>Error</option>
              <option value={LogLevel.Critical}>Critical</option>
            </select>
          </div>
        </div>
      </div>
      <div className="flex-1 overflow-auto p-4">
        <div className="space-y-2">
          {logs.map((log) => (
            <div
              key={log.id}
              className="bg-slate-800 border border-slate-700 rounded p-3 hover:border-slate-600 transition-colors"
            >
              <div className="flex items-center gap-3 mb-2">
                {getLevelIcon(log.level)}
                <span className={`px-2 py-1 rounded text-xs font-medium ${getLevelBadgeColor(log.level)}`}>
                  {LogLevel[log.level]}
                </span>
                <span className="text-gray-400 text-sm">{log.serviceName}</span>
                <span className="text-gray-500 text-xs ml-auto">{formatTimestamp(log.createdAtUtc)}</span>
              </div>
              <p className={`text-sm ${getLevelColor(log.level)} break-all`}>{log.message}</p>
              {log.stackTrace && (
                <details className="mt-2">
                  <summary className="text-xs text-gray-500 cursor-pointer hover:text-gray-400">
                    Stack Trace
                  </summary>
                  <pre className="mt-2 text-xs text-gray-600 bg-slate-900 p-2 rounded overflow-x-auto">
                    {log.stackTrace}
                  </pre>
                </details>
              )}
            </div>
          ))}
          {logs.length === 0 && (
            <div className="text-center text-gray-500 py-8">
              No logs to display
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default LogStream;
