import React, { useState, useEffect } from 'react';
import LogStream from './components/LogStream';
import ErrorGroups from './components/ErrorGroups';
import { useLogsStream } from './hooks/useLogsStream';
import { Activity, Zap, AlertTriangle, X } from 'lucide-react';
import * as signalR from '@microsoft/signalr';

interface ServiceMetric {
  serviceName: string;
  timestamp: string;
  rps: number;
  errorRate: number;
}

interface Alert {
  service: string;
  message: string;
}

const App: React.FC = () => {
  const {
    logs,
    isConnected,
    filterService,
    setFilterService,
    filterLevel,
    setFilterLevel,
  } = useLogsStream();

  const [metrics, setMetrics] = useState<ServiceMetric[]>([]);
  const [alert, setAlert] = useState<Alert | null>(null);
  const [connection, setConnection] = useState<signalR.HubConnection | null>(null);

  useEffect(() => {
    const newConnection = new signalR.HubConnectionBuilder()
      .withUrl('http://localhost:5000/logs-stream')
      .withAutomaticReconnect()
      .build();

    setConnection(newConnection);

    newConnection
      .start()
      .then(() => {
        console.log('SignalR Connected');
      })
      .catch((err) => console.error('SignalR Connection Error: ', err));

    newConnection.on('ReceiveMetrics', (receivedMetrics: ServiceMetric[]) => {
      setMetrics(receivedMetrics);
    });

    newConnection.on('ReceiveAlert', (receivedAlert: Alert) => {
      setAlert(receivedAlert);
      setTimeout(() => setAlert(null), 5000);
    });

    return () => {
      newConnection.off('ReceiveMetrics');
      newConnection.off('ReceiveAlert');
      newConnection.stop();
    };
  }, []);

  return (
    <div className="h-screen flex flex-col bg-slate-900">
      {alert && (
        <div className="bg-red-600 text-white px-6 py-4 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <AlertTriangle className="w-6 h-6" />
            <span className="font-bold">{alert.service}: {alert.message}</span>
          </div>
          <button onClick={() => setAlert(null)} className="hover:bg-red-700 p-1 rounded">
            <X className="w-5 h-5" />
          </button>
        </div>
      )}
      <header className="bg-slate-800 border-b border-slate-700 px-6 py-4">
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-3">
            <Activity className="w-8 h-8 text-blue-400" />
            <h1 className="text-2xl font-bold text-white">AI Log Observability</h1>
          </div>
          <div className="flex items-center gap-2">
            <div className={`w-2 h-2 rounded-full ${isConnected ? 'bg-green-500' : 'bg-red-500'}`} />
            <span className="text-sm text-gray-400">
              {isConnected ? 'Connected' : 'Disconnected'}
            </span>
          </div>
        </div>
        <div className="grid grid-cols-3 gap-4">
          {metrics.map((metric) => (
            <div key={metric.serviceName} className="bg-slate-700 rounded p-3">
              <div className="text-sm text-gray-400 mb-2">{metric.serviceName}</div>
              <div className="space-y-2">
                <div>
                  <div className="text-xs text-gray-500 mb-1">RPS</div>
                  <div className="h-2 bg-slate-600 rounded overflow-hidden">
                    <div 
                      className="h-full bg-blue-500 transition-all duration-300"
                      style={{ width: `${Math.min(metric.rps * 10, 100)}%` }}
                    />
                  </div>
                  <div className="text-xs text-white mt-1">{metric.rps.toFixed(1)}</div>
                </div>
                <div>
                  <div className="text-xs text-gray-500 mb-1">Error Rate</div>
                  <div className="h-2 bg-slate-600 rounded overflow-hidden">
                    <div 
                      className={`h-full transition-all duration-300 ${metric.errorRate > 50 ? 'bg-red-500' : 'bg-green-500'}`}
                      style={{ width: `${Math.min(metric.errorRate, 100)}%` }}
                    />
                  </div>
                  <div className={`text-xs mt-1 ${metric.errorRate > 50 ? 'text-red-400' : 'text-green-400'}`}>
                    {metric.errorRate.toFixed(1)}%
                  </div>
                </div>
              </div>
            </div>
          ))}
        </div>
      </header>
      <div className="flex-1 flex overflow-hidden">
        <div className="flex-1 border-r border-slate-700">
          <LogStream
            logs={logs}
            filterService={filterService}
            setFilterService={setFilterService}
            filterLevel={filterLevel}
            setFilterLevel={setFilterLevel}
          />
        </div>
        <div className="w-96 min-w-96">
          <ErrorGroups />
        </div>
      </div>
      <footer className="bg-slate-800 border-t border-slate-700 px-6 py-2">
        <div className="flex items-center justify-between text-xs text-gray-500">
          <div className="flex items-center gap-2">
            <Zap className="w-4 h-4" />
            <span>Powered by Mistral AI</span>
          </div>
          <span>Total logs: {logs.length}</span>
        </div>
      </footer>
    </div>
  );
};

export default App;
