import React, { useState, useEffect, useRef } from 'react';
import { getLogs } from '../api';

const LogsPage = () => {
    const [logs, setLogs] = useState([]);
    const [loading, setLoading] = useState(true);
    const [autoRefresh, setAutoRefresh] = useState(true);
    const [filter, setFilter] = useState('all'); // all, info, warning, error
    const logsEndRef = useRef(null);

    const scrollToBottom = () => {
        logsEndRef.current?.scrollIntoView({ behavior: "smooth" });
    };

    useEffect(() => {
        scrollToBottom();
    }, [logs]);

    useEffect(() => {
        loadLogs();

        let interval;
        if (autoRefresh) {
            interval = setInterval(loadLogs, 5000); // Обновление каждые 5 сек
        }

        return () => clearInterval(interval);
    }, [autoRefresh]);

    const loadLogs = async () => {
        try {
            const logsData = await getLogs();
            setLogs(logsData);
        } catch (error) {
            console.error('Error loading logs:', error);
            // Заглушка если API еще не готово
            setLogs([
                {
                    id: 1,
                    timestamp: new Date().toISOString(),
                    level: 'INFO',
                    message: 'Bot started successfully',
                    source: 'BotService'
                },
                {
                    id: 2,
                    timestamp: new Date().toISOString(),
                    level: 'INFO',
                    message: 'Received message from user 123456',
                    source: 'MessageHandler'
                },
                {
                    id: 3,
                    timestamp: new Date().toISOString(),
                    level: 'ERROR',
                    message: 'Command execution failed: Timeout',
                    source: 'CommandExecutor'
                }
            ]);
        } finally {
            setLoading(false);
        }
    };

    const getLogLevelColor = (level) => {
        switch (level?.toUpperCase()) {
            case 'ERROR': return '#e74c3c';
            case 'WARNING': return '#f39c12';
            case 'INFO': return '#3498db';
            default: return '#95a5a6';
        }
    };

    const getLogLevelBg = (level) => {
        switch (level?.toUpperCase()) {
            case 'ERROR': return '#fadbd8';
            case 'WARNING': return '#fdebd0';
            case 'INFO': return '#d6eaf8';
            default: return '#f8f9fa';
        }
    };

    const filteredLogs = logs.filter(log =>
        filter === 'all' || log.level?.toUpperCase() === filter.toUpperCase()
    );

    if (loading) {
        return (
            <div style={{ padding: '20px' }}>
                <div>Loading logs...</div>
            </div>
        );
    }

    return (
        <div style={{ maxWidth: '1200px', margin: '0 auto' }}>
            <div style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                marginBottom: '20px',
                flexWrap: 'wrap',
                gap: '15px'
            }}>
                <h1 style={{ color: '#2c3e50', margin: 0 }}>Logs</h1>

                <div style={{ display: 'flex', gap: '15px', alignItems: 'center', flexWrap: 'wrap' }}>
                    {/* Фильтры по уровню */}
                    <select
                        value={filter}
                        onChange={(e) => setFilter(e.target.value)}
                        style={{
                            padding: '8px 12px',
                            border: '1px solid #ddd',
                            borderRadius: '4px',
                            background: 'white'
                        }}
                    >
                        <option value="all">All Levels</option>
                        <option value="info">Info</option>
                        <option value="warning">Warning</option>
                        <option value="error">Error</option>
                    </select>

                    {/* Автообновление */}
                    <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer' }}>
                        <input
                            type="checkbox"
                            checked={autoRefresh}
                            onChange={(e) => setAutoRefresh(e.target.checked)}
                        />
                        Auto-refresh (5s)
                    </label>

                    {/* Кнопка обновления */}
                    <button
                        onClick={loadLogs}
                        style={{
                            background: '#3498db',
                            color: 'white',
                            border: 'none',
                            padding: '8px 16px',
                            borderRadius: '4px',
                            cursor: 'pointer'
                        }}
                    >
                        Refresh
                    </button>
                </div>
            </div>

            <div style={{
                background: '#fff',
                borderRadius: '8px',
                boxShadow: '0 2px 4px rgba(0,0,0,0.1)',
                border: '1px solid #e0e0e0',
                overflow: 'hidden'
            }}>
                <div style={{
                    maxHeight: '600px',
                    overflowY: 'auto',
                    padding: '10px'
                }}>
                    {filteredLogs.length === 0 ? (
                        <div style={{
                            padding: '40px',
                            textAlign: 'center',
                            color: '#7f8c8d'
                        }}>
                            No logs found
                        </div>
                    ) : (
                        filteredLogs.map((log, index) => (
                            <div
                                key={log.id || index}
                                style={{
                                    padding: '12px',
                                    borderBottom: '1px solid #f0f0f0',
                                    background: getLogLevelBg(log.level),
                                    marginBottom: '4px',
                                    borderRadius: '4px',
                                    fontFamily: 'monospace',
                                    fontSize: '13px'
                                }}
                            >
                                <div style={{
                                    display: 'flex',
                                    alignItems: 'flex-start',
                                    gap: '10px',
                                    flexWrap: 'wrap'
                                }}>
                                    <span style={{
                                        color: '#7f8c8d',
                                        minWidth: '160px',
                                        fontSize: '12px'
                                    }}>
                                        {new Date(log.timestamp).toLocaleString()}
                                    </span>

                                    <span style={{
                                        background: getLogLevelColor(log.level),
                                        color: 'white',
                                        padding: '2px 8px',
                                        borderRadius: '12px',
                                        fontSize: '11px',
                                        fontWeight: 'bold',
                                        minWidth: '60px',
                                        textAlign: 'center'
                                    }}>
                                        {log.level}
                                    </span>

                                    {log.source && (
                                        <span style={{
                                            color: '#2c3e50',
                                            fontWeight: 'bold',
                                            fontSize: '12px',
                                            minWidth: '120px'
                                        }}>
                                            [{log.source}]
                                        </span>
                                    )}

                                    <span style={{
                                        color: '#2c3e50',
                                        flex: 1,
                                        wordBreak: 'break-word'
                                    }}>
                                        {log.message}
                                    </span>
                                </div>
                            </div>
                        ))
                    )}
                    <div ref={logsEndRef} />
                </div>
            </div>

            <div style={{
                marginTop: '15px',
                color: '#7f8c8d',
                fontSize: '12px',
                textAlign: 'center'
            }}>
                Showing {filteredLogs.length} of {logs.length} logs
                {autoRefresh && ' • Auto-refresh enabled'}
            </div>
        </div>
    );
};

export default LogsPage;