import React, { useState, useEffect } from 'react';
import { ErrorGroup } from '../types';
import { Brain, ChevronDown, ChevronUp, AlertTriangle, CheckCircle, Loader2 } from 'lucide-react';
import * as signalR from '@microsoft/signalr';

interface ErrorGroupsProps {
  connection: signalR.HubConnection | null;
}

const ErrorGroups: React.FC<ErrorGroupsProps> = ({ connection }) => {
  const [errorGroups, setErrorGroups] = useState<ErrorGroup[]>([]);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [applyingPatch, setApplyingPatch] = useState<string | null>(null);
  const [patchApplied, setPatchApplied] = useState<Set<string>>(new Set());

  useEffect(() => {
    const fetchErrorGroups = async () => {
      try {
        const response = await fetch('http://localhost:5000/api/errors/groups');
        const data: ErrorGroup[] = await response.json();
        setErrorGroups(data);
      } catch (error) {
        console.error('Failed to load error groups:', error);
      } finally {
        setLoading(false);
      }
    };

    fetchErrorGroups();

    if (connection) {
      connection.on('ReceiveErrorGroup', (receivedGroup: ErrorGroup) => {
        console.log('Received error group via SignalR:', receivedGroup);
        setErrorGroups(prevGroups => {
          const existingIndex = prevGroups.findIndex(g => g.id === receivedGroup.id);
          if (existingIndex >= 0) {
            const updated = [...prevGroups];
            updated[existingIndex] = receivedGroup;
            return updated;
          } else {
            return [...prevGroups, receivedGroup];
          }
        });
      });
    }

    const interval = setInterval(fetchErrorGroups, 10000);
    return () => {
      clearInterval(interval);
      if (connection) {
        connection.off('ReceiveErrorGroup');
      }
    };
  }, [connection]);

  const formatTimestamp = (timestamp: string) => {
    const date = new Date(timestamp);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMins = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMs / 3600000);
    const diffDays = Math.floor(diffMs / 86400000);

    if (diffMins < 1) return 'Just now';
    if (diffMins < 60) return `${diffMins}m ago`;
    if (diffHours < 24) return `${diffHours}h ago`;
    return `${diffDays}d ago`;
  };

  const toggleExpand = (id: string) => {
    setExpandedId(expandedId === id ? null : id);
  };

  const applyPatch = async (id: string) => {
    setApplyingPatch(id);
    try {
      const response = await fetch(`http://localhost:5000/api/errors/${id}/apply-patch`, {
        method: 'POST',
      });
      const data = await response.json();
      if (response.ok) {
        setPatchApplied(new Set([...patchApplied, id]));
      }
    } catch (error) {
      console.error('Failed to apply patch:', error);
    } finally {
      setApplyingPatch(null);
    }
  };

  if (loading) {
    return (
      <div className="flex flex-col h-full">
        <div className="bg-slate-800 border-b border-slate-700 p-4">
          <h2 className="text-xl font-bold text-white flex items-center gap-2">
            <Brain className="w-5 h-5 text-purple-400" />
            AI Error Analysis
          </h2>
        </div>
        <div className="flex-1 flex items-center justify-center">
          <div className="text-gray-500">Loading error groups...</div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full">
      <div className="bg-slate-800 border-b border-slate-700 p-4">
        <h2 className="text-xl font-bold text-white flex items-center gap-2">
          <Brain className="w-5 h-5 text-purple-400" />
          AI Error Analysis
        </h2>
        <p className="text-sm text-gray-400 mt-1">Aggregated errors with AI insights and auto-fix suggestions</p>
      </div>
      <div className="flex-1 overflow-auto p-4">
        <div className="space-y-3">
          {errorGroups.length === 0 && (
            <div className="text-center text-gray-500 py-8">
              No error groups detected
            </div>
          )}
          {errorGroups.map((group) => (
            <div
              key={group.id}
              className="bg-slate-800 border border-slate-700 rounded-lg overflow-hidden hover:border-purple-500 transition-colors cursor-pointer"
              onClick={() => toggleExpand(group.id)}
            >
              <div className="p-4">
                <div className="flex items-start justify-between mb-2">
                  <div className="flex items-center gap-2 flex-1">
                    <AlertTriangle className="w-4 h-4 text-red-500 flex-shrink-0" />
                    <span className="font-semibold text-white text-sm break-all">
                      {group.errorClass}
                    </span>
                  </div>
                  <div className="flex items-center gap-2 flex-shrink-0 ml-2">
                    <span className="bg-red-900 text-red-300 px-2 py-1 rounded text-xs font-bold">
                      {group.count}
                    </span>
                    {expandedId === group.id ? (
                      <ChevronUp className="w-4 h-4 text-gray-400" />
                    ) : (
                      <ChevronDown className="w-4 h-4 text-gray-400" />
                    )}
                  </div>
                </div>
                <div className="text-xs text-gray-500">
                  Last seen: {formatTimestamp(group.lastSeenUtc)}
                </div>
              </div>
              {expandedId === group.id && (
                <div className="border-t border-slate-700 p-4 bg-slate-900/50">
                  <div className="mb-3">
                    <div className="text-xs text-gray-400 mb-1">First seen</div>
                    <div className="text-sm text-gray-300">
                      {new Date(group.firstSeenUtc).toLocaleString('en-US')}
                    </div>
                  </div>
                  <div className="mb-3">
                    <div className="text-xs text-gray-400 mb-1">Occurrences</div>
                    <div className="text-sm text-gray-300">{group.count} times</div>
                  </div>
                  <div className="border-2 border-purple-500/30 rounded-lg p-3 bg-purple-900/10 mb-3">
                    <div className="flex items-center gap-2 mb-2">
                      <Brain className="w-4 h-4 text-purple-400" />
                      <span className="text-sm font-semibold text-purple-300">
                        AI Analysis (Mistral)
                      </span>
                    </div>
                    <p className="text-sm text-gray-300 leading-relaxed">
                      {group.summary}
                    </p>
                  </div>
                  {group.suggestedPatch && group.suggestedPatch !== '// Patch not generated' && (
                    <div className="mb-3">
                      <div className="text-xs text-gray-400 mb-2">Suggested Fix</div>
                      <div className="bg-slate-950 rounded-lg p-3 border border-slate-700 overflow-x-auto">
                        <pre className="text-xs text-green-400 font-mono whitespace-pre-wrap">
                          {group.suggestedPatch}
                        </pre>
                      </div>
                    </div>
                  )}
                  {group.suggestedPatch && group.suggestedPatch !== '// Patch not generated' && (
                    <button
                      onClick={(e) => {
                        e.stopPropagation();
                        applyPatch(group.id);
                      }}
                      disabled={applyingPatch === group.id || patchApplied.has(group.id)}
                      className="w-full mt-2 bg-gradient-to-r from-purple-600 to-blue-600 hover:from-purple-700 hover:to-blue-700 disabled:from-gray-600 disabled:to-gray-600 text-white px-4 py-2 rounded-lg font-semibold flex items-center justify-center gap-2 transition-colors"
                    >
                      {applyingPatch === group.id ? (
                        <>
                          <Loader2 className="w-4 h-4 animate-spin" />
                          Applying...
                        </>
                      ) : patchApplied.has(group.id) ? (
                        <>
                          <CheckCircle className="w-4 h-4" />
                          Patch applied! 🚀
                        </>
                      ) : (
                        <>
                          🤖 Create Pull Request (Auto-Fix)
                        </>
                      )}
                    </button>
                  )}
                </div>
              )}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
};

export default ErrorGroups;
