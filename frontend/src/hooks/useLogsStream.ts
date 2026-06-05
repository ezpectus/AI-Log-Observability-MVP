import { useState, useEffect, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import { LogEntry, LogLevel } from '../types';

export const useLogsStream = () => {
  const [logs, setLogs] = useState<LogEntry[]>([]);
  const [isConnected, setIsConnected] = useState(false);
  const [filterService, setFilterService] = useState<string>('');
  const [filterLevel, setFilterLevel] = useState<LogLevel | null>(null);

  const loadInitialLogs = useCallback(async () => {
    try {
      const response = await fetch('http://localhost:5000/api/logs?limit=50');
      const data: LogEntry[] = await response.json();
      setLogs(data);
    } catch (error) {
      console.error('Failed to load initial logs:', error);
    }
  }, []);

  useEffect(() => {
    const newConnection = new signalR.HubConnectionBuilder()
      .withUrl('http://localhost:5000/logs-stream')
      .withAutomaticReconnect()
      .build();

    newConnection
      .start()
      .then(() => {
        console.log('SignalR Connected');
        setIsConnected(true);
        loadInitialLogs();
      })
      .catch((err) => console.error('SignalR Connection Error: ', err));

    newConnection.on('ReceiveLog', (receivedLog: LogEntry) => {
      setLogs((prevLogs) => [receivedLog, ...prevLogs]);
    });

    return () => {
      newConnection.stop();
    };
  }, [loadInitialLogs]);

  const filteredLogs = logs.filter((log) => {
    if (filterService && !log.serviceName.toLowerCase().includes(filterService.toLowerCase())) {
      return false;
    }
    if (filterLevel !== null && log.level !== filterLevel) {
      return false;
    }
    return true;
  });

  return {
    logs: filteredLogs,
    isConnected,
    filterService,
    setFilterService,
    filterLevel,
    setFilterLevel,
  };
};
