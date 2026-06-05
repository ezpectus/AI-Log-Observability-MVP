import React, { useCallback, useEffect, useMemo, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import {
  AlertTriangle,
  Brain,
  CheckCircle,
  ChevronDown,
  ChevronUp,
  Loader2,
  RefreshCw,
} from 'lucide-react';
import { ErrorGroup } from '../types';

interface ErrorGroupsProps {
  connection: signalR.HubConnection | null;
}

type ErrorGroupPayload = Partial<ErrorGroup> & {
  Id?: string;
  ErrorClass?: string;
  Summary?: string;
  SuggestedPatch?: string;
  FirstSeenUtc?: string;
  LastSeenUtc?: string;
  Count?: number;
};

const API_BASE_URL = 'http://localhost:5000';
const ERROR_GROUPS_URL = `${API_BASE_URL}/api/logs/errors/groups`;

const normalizeErrorGroup = (payload: ErrorGroupPayload): ErrorGroup | null => {
  const id = payload.id ?? payload.Id;
  const errorClass = payload.errorClass ?? payload.ErrorClass;

  if (!id || !errorClass) {
    return null;
  }

  return {
    id,
    errorClass,
    summary: payload.summary ?? payload.Summary ?? 'AI analysis is not available yet.',
    suggestedPatch: payload.suggestedPatch ?? payload.SuggestedPatch ?? '',
    firstSeenUtc: payload.firstSeenUtc ?? payload.FirstSeenUtc ?? new Date().toISOString(),
    lastSeenUtc: payload.lastSeenUtc ?? payload.LastSeenUtc ?? new Date().toISOString(),
    count: payload.count ?? payload.Count ?? 0,
  };
};

const hasSuggestedPatch = (group: ErrorGroup) => {
  const patch = group.suggestedPatch?.trim();
  return Boolean(patch && patch !== '// Patch not generated');
};

const formatTimestamp = (timestamp: string) => {
  const date = new Date(timestamp);

  if (Number.isNaN(date.getTime())) {
    return 'Unknown';
  }

  const now = new Date();
  const diffMs = Math.max(now.getTime() - date.getTime(), 0);
  const diffMins = Math.floor(diffMs / 60_000);
  const diffHours = Math.floor(diffMs / 3_600_000);
  const diffDays = Math.floor(diffMs / 86_400_000);

  if (diffMins < 1) return 'Just now';
  if (diffMins < 60) return `${diffMins}m ago`;
  if (diffHours < 24) return `${diffHours}h ago`;
  return `${diffDays}d ago`;
};

const sortByLastSeenDescending = (groups: ErrorGroup[]) =>
  [...groups].sort((a, b) => {
    const aTime = new Date(a.lastSeenUtc).getTime();
    const bTime = new Date(b.lastSeenUtc).getTime();
    return (Number.isNaN(bTime) ? 0 : bTime) - (Number.isNaN(aTime) ? 0 : aTime);
  });

const ErrorGroups: React.FC<ErrorGroupsProps> = ({ connection }) => {
  const [errorGroups, setErrorGroups] = useState<ErrorGroup[]>([]);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [applyingPatch, setApplyingPatch] = useState<string | null>(null);
  const [patchApplied, setPatchApplied] = useState<Set<string>>(() => new Set());

  const visibleErrorGroups = useMemo(
    () => sortByLastSeenDescending(errorGroups),
    [errorGroups],
  );

  const upsertErrorGroup = useCallback((group: ErrorGroup) => {
    setErrorGroups((prevGroups) => {
      const existingIndex = prevGroups.findIndex((existingGroup) => existingGroup.id === group.id);

      if (existingIndex === -1) {
        return sortByLastSeenDescending([...prevGroups, group]);
      }

      const updatedGroups = [...prevGroups];
      updatedGroups[existingIndex] = group;
      return sortByLastSeenDescending(updatedGroups);
    });
  }, []);

  const fetchErrorGroups = useCallback(async (showRefreshingState = false) => {
    if (showRefreshingState) {
      setRefreshing(true);
    }

    try {
      setLoadError(null);

      const response = await fetch(ERROR_GROUPS_URL);

      if (!response.ok) {
        throw new Error(`Request failed with HTTP ${response.status}`);
      }

      const payload = (await response.json()) as ErrorGroupPayload[];
      const normalizedGroups = payload
        .map(normalizeErrorGroup)
        .filter((group): group is ErrorGroup => group !== null);

      setErrorGroups(sortByLastSeenDescending(normalizedGroups));
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Unable to load error groups';
      setLoadError(message);
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, []);

  useEffect(() => {
    fetchErrorGroups();
    const intervalId = window.setInterval(() => fetchErrorGroups(), 10_000);

    return () => {
      window.clearInterval(intervalId);
    };
  }, [fetchErrorGroups]);

  useEffect(() => {
    if (!connection) {
      return;
    }

    const receiveErrorGroup = (payload: ErrorGroupPayload) => {
      const group = normalizeErrorGroup(payload);

      if (group) {
        upsertErrorGroup(group);
      }
    };

    connection.on('ReceiveErrorGroup', receiveErrorGroup);

    return () => {
      connection.off('ReceiveErrorGroup', receiveErrorGroup);
    };
  }, [connection, upsertErrorGroup]);

  const toggleExpand = (id: string) => {
    setExpandedId((currentId) => (currentId === id ? null : id));
  };

  const applyPatch = async (id: string) => {
    setApplyingPatch(id);

    try {
      const response = await fetch(`${API_BASE_URL}/api/logs/errors/${id}/apply-patch`, {
        method: 'POST',
      });

      if (!response.ok) {
        throw new Error(`Request failed with HTTP ${response.status}`);
      }

      setPatchApplied((previous) => {
        const next = new Set(previous);
        next.add(id);
        return next;
      });
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Failed to apply patch';
      setLoadError(message);
    } finally {
      setApplyingPatch(null);
    }
  };

  return (
    <div className="flex h-full flex-col bg-slate-900">
      <div className="border-b border-slate-700 bg-slate-800 p-4">
        <div className="flex items-start justify-between gap-3">
          <div>
            <h2 className="flex items-center gap-2 text-xl font-bold text-white">
              <Brain className="h-5 w-5 text-purple-400" />
              AI Error Analysis
            </h2>
            <p className="mt-1 text-sm text-gray-400">
              Aggregated errors with AI insights and fix suggestions
            </p>
          </div>
          <button
            type="button"
            onClick={() => fetchErrorGroups(true)}
            disabled={loading || refreshing}
            className="rounded border border-slate-600 p-2 text-gray-300 transition-colors hover:border-purple-400 hover:text-white disabled:cursor-not-allowed disabled:opacity-50"
            aria-label="Refresh error groups"
            title="Refresh error groups"
          >
            <RefreshCw className={`h-4 w-4 ${refreshing ? 'animate-spin' : ''}`} />
          </button>
        </div>
      </div>

      <div className="flex-1 overflow-auto p-4">
        {loading ? (
          <div className="flex h-full items-center justify-center text-sm text-gray-500">
            Loading error groups...
          </div>
        ) : (
          <div className="space-y-3">
            {loadError && (
              <div className="rounded border border-red-500/40 bg-red-950/40 p-3 text-sm text-red-200">
                {loadError}
              </div>
            )}

            {visibleErrorGroups.length === 0 ? (
              <div className="rounded border border-dashed border-slate-700 p-8 text-center text-sm text-gray-500">
                No error groups detected
              </div>
            ) : (
              visibleErrorGroups.map((group) => {
                const expanded = expandedId === group.id;
                const patchAvailable = hasSuggestedPatch(group);
                const firstSeenDate = new Date(group.firstSeenUtc);

                return (
                  <div
                    key={group.id}
                    className="overflow-hidden rounded border border-slate-700 bg-slate-800 transition-colors hover:border-purple-500"
                  >
                    <button
                      type="button"
                      onClick={() => toggleExpand(group.id)}
                      className="w-full p-4 text-left"
                      aria-expanded={expanded}
                    >
                      <div className="mb-2 flex items-start justify-between gap-3">
                        <div className="flex min-w-0 flex-1 items-center gap-2">
                          <AlertTriangle className="h-4 w-4 flex-shrink-0 text-red-500" />
                          <span className="break-words text-sm font-semibold text-white">
                            {group.errorClass}
                          </span>
                        </div>
                        <div className="flex flex-shrink-0 items-center gap-2">
                          <span className="rounded bg-red-900 px-2 py-1 text-xs font-bold text-red-300">
                            {group.count}
                          </span>
                          {expanded ? (
                            <ChevronUp className="h-4 w-4 text-gray-400" />
                          ) : (
                            <ChevronDown className="h-4 w-4 text-gray-400" />
                          )}
                        </div>
                      </div>
                      <div className="text-xs text-gray-500">
                        Last seen: {formatTimestamp(group.lastSeenUtc)}
                      </div>
                    </button>

                    {expanded && (
                      <div className="border-t border-slate-700 bg-slate-900/50 p-4">
                        <div className="mb-3 grid grid-cols-2 gap-3">
                          <div>
                            <div className="mb-1 text-xs text-gray-400">First seen</div>
                            <div className="text-sm text-gray-300">
                              {Number.isNaN(firstSeenDate.getTime())
                                ? 'Unknown'
                                : firstSeenDate.toLocaleString()}
                            </div>
                          </div>
                          <div>
                            <div className="mb-1 text-xs text-gray-400">Occurrences</div>
                            <div className="text-sm text-gray-300">{group.count} times</div>
                          </div>
                        </div>

                        <div className="mb-3 rounded border border-purple-500/30 bg-purple-950/20 p-3">
                          <div className="mb-2 flex items-center gap-2">
                            <Brain className="h-4 w-4 text-purple-400" />
                            <span className="text-sm font-semibold text-purple-300">
                              AI Analysis
                            </span>
                          </div>
                          <p className="whitespace-pre-wrap text-sm leading-relaxed text-gray-300">
                            {group.summary || 'AI analysis is not available yet.'}
                          </p>
                        </div>

                        {patchAvailable && (
                          <div className="mb-3">
                            <div className="mb-2 text-xs text-gray-400">Suggested fix</div>
                            <div className="overflow-x-auto rounded border border-slate-700 bg-slate-950 p-3">
                              <pre className="whitespace-pre-wrap text-xs font-mono text-green-400">
                                {group.suggestedPatch}
                              </pre>
                            </div>
                          </div>
                        )}

                        {patchAvailable && (
                          <button
                            type="button"
                            onClick={(event) => {
                              event.stopPropagation();
                              applyPatch(group.id);
                            }}
                            disabled={applyingPatch === group.id || patchApplied.has(group.id)}
                            className="mt-2 flex w-full items-center justify-center gap-2 rounded bg-purple-600 px-4 py-2 font-semibold text-white transition-colors hover:bg-purple-700 disabled:cursor-not-allowed disabled:bg-slate-600"
                          >
                            {applyingPatch === group.id ? (
                              <>
                                <Loader2 className="h-4 w-4 animate-spin" />
                                Applying...
                              </>
                            ) : patchApplied.has(group.id) ? (
                              <>
                                <CheckCircle className="h-4 w-4" />
                                Patch applied
                              </>
                            ) : (
                              'Create Pull Request'
                            )}
                          </button>
                        )}
                      </div>
                    )}
                  </div>
                );
              })
            )}
          </div>
        )}
      </div>
    </div>
  );
};

export default ErrorGroups;
