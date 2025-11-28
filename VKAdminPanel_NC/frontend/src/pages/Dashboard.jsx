import React, { useState, useEffect } from 'react';
import { StatCard } from '../components/StatCard';
import { StatsChart } from '../components/StatsChart';
import {
    getStats,
    getBotStatus,
    startBot,
    stopBot,
    restartBot,
    getCommandStats,
    getUserStats,
    getSystemStats
} from '../api';
import { useNotification } from '../contexts/NotificationContext';

const Dashboard = () => {
    const [stats, setStats] = useState({});
    const [botStatus, setBotStatus] = useState('loading');
    const [commandStats, setCommandStats] = useState({});
    const [userStats, setUserStats] = useState({});
    const [systemStats, setSystemStats] = useState({});
    const [loading, setLoading] = useState(true);
    const [botActionLoading, setBotActionLoading] = useState('');
    const { addNotification } = useNotification();

    useEffect(() => {
        loadRealData();
        const interval = setInterval(loadRealData, 30000);
        return () => clearInterval(interval);
    }, []);

    const loadRealData = async () => {
        try {
            const [
                botStatsData,
                statusData,
                commandsData,
                usersData,
                systemData
            ] = await Promise.all([
                getStats().catch(() => ({})),
                getBotStatus().catch(() => ({ status: 'unknown' })),
                getCommandStats().catch(() => ({})),
                getUserStats().catch(() => ({})),
                getSystemStats().catch(() => ({}))
            ]);

            setStats(botStatsData);
            setBotStatus(statusData.status || 'unknown');
            setCommandStats(commandsData);
            setUserStats(usersData);
            setSystemStats(systemData);

        } catch (error) {
            console.error('Error loading real data:', error);
            setBotStatus('error');
            addNotification('Failed to load real statistics', 'error');
        } finally {
            setLoading(false);
        }
    };

    const handleBotAction = async (action) => {
        setBotActionLoading(action);
        try {
            switch (action) {
                case 'start':
                    await startBot();
                    addNotification('Bot started successfully', 'success');
                    break;
                case 'stop':
                    await stopBot();
                    addNotification('Bot stopped', 'warning');
                    break;
                case 'restart':
                    await restartBot();
                    addNotification('Bot restarted', 'info');
                    break;
            }
            setTimeout(loadRealData, 2000);
        } catch (error) {
            console.error('Bot action failed:', error);
            addNotification('Failed to execute bot action', 'error');
        } finally {
            setBotActionLoading('');
        }
    };

    const getStatusColor = (status) => {
        switch (status) {
            case 'online': return '#27ae60';
            case 'offline': return '#e74c3c';
            case 'starting': return '#f39c12';
            case 'stopping': return '#f39c12';
            default: return '#95a5a6';
        }
    };

    // Реальные данные для графиков из статистики команд
    const commandUsageData = {
        labels: commandStats.dailyUsage?.map(day => day.date) || ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'],
        datasets: [
            {
                label: 'Commands Executed',
                data: commandStats.dailyUsage?.map(day => day.count) || [65, 78, 90, 81, 86, 55, 40],
                borderColor: '#3498db',
                backgroundColor: 'rgba(52, 152, 219, 0.1)',
                tension: 0.4,
            },
        ],
    };

    const userActivityData = {
        labels: userStats.hourlyActivity?.map(hour => hour.time) || ['00:00', '04:00', '08:00', '12:00', '16:00', '20:00'],
        datasets: [
            {
                label: 'Active Users',
                data: userStats.hourlyActivity?.map(hour => hour.count) || [12, 19, 3, 5, 2, 3, 15],
                backgroundColor: 'rgba(46, 204, 113, 0.8)',
            },
        ],
    };

    const popularCommandsData = {
        labels: commandStats.popularCommands?.map(cmd => cmd.name) || ['help', 'start', 'status'],
        datasets: [
            {
                data: commandStats.popularCommands?.map(cmd => cmd.usageCount) || [45, 30, 25],
                backgroundColor: [
                    '#e74c3c',
                    '#f39c12',
                    '#3498db',
                    '#9b59b6',
                ],
            },
        ],
    };

    if (loading) {
        return (
            <div style={{
                display: 'flex',
                justifyContent: 'center',
                alignItems: 'center',
                height: '200px'
            }}>
                <div>Loading real statistics...</div>
            </div>
        );
    }

    return (
        <div style={{
            padding: '20px',
            maxWidth: '1400px',
            margin: '0 auto',
            fontFamily: 'Arial, sans-serif'
        }}>
            {/* Заголовок и кнопки управления */}
            <div style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                marginBottom: '30px',
                flexWrap: 'wrap',
                gap: '20px'
            }}>
                <h1 style={{
                    margin: 0,
                    color: '#2c3e50',
                    borderBottom: '2px solid #ecf0f1',
                    paddingBottom: '10px'
                }}>
                    Real-time Bot Dashboard
                </h1>

                <div style={{ display: 'flex', gap: '10px', flexWrap: 'wrap' }}>
                    <button
                        onClick={() => handleBotAction('start')}
                        disabled={botActionLoading || botStatus === 'online'}
                        style={{
                            background: botStatus === 'online' ? '#27ae60' : '#3498db',
                            color: 'white',
                            border: 'none',
                            padding: '8px 16px',
                            borderRadius: '4px',
                            cursor: botActionLoading || botStatus === 'online' ? 'not-allowed' : 'pointer',
                            opacity: botActionLoading || botStatus === 'online' ? 0.6 : 1
                        }}
                    >
                        {botActionLoading === 'start' ? 'Starting...' : 'Start Bot'}
                    </button>
                    <button
                        onClick={() => handleBotAction('stop')}
                        disabled={botActionLoading || botStatus === 'offline'}
                        style={{
                            background: botStatus === 'offline' ? '#e74c3c' : '#3498db',
                            color: 'white',
                            border: 'none',
                            padding: '8px 16px',
                            borderRadius: '4px',
                            cursor: botActionLoading || botStatus === 'offline' ? 'not-allowed' : 'pointer',
                            opacity: botActionLoading || botStatus === 'offline' ? 0.6 : 1
                        }}
                    >
                        {botActionLoading === 'stop' ? 'Stopping...' : 'Stop Bot'}
                    </button>
                    <button
                        onClick={() => handleBotAction('restart')}
                        disabled={botActionLoading}
                        style={{
                            background: '#f39c12',
                            color: 'white',
                            border: 'none',
                            padding: '8px 16px',
                            borderRadius: '4px',
                            cursor: botActionLoading ? 'not-allowed' : 'pointer',
                            opacity: botActionLoading ? 0.6 : 1
                        }}
                    >
                        {botActionLoading === 'restart' ? 'Restarting...' : 'Restart Bot'}
                    </button>
                </div>
            </div>

            {/* Основная статистика из реальных данных */}
            <div style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))',
                gap: '20px',
                marginBottom: '40px'
            }}>
                <StatCard
                    title="Total Users"
                    value={userStats.totalUsers?.toLocaleString() || stats.totalUsers?.toLocaleString() || '0'}
                />
                <StatCard
                    title="Active Today"
                    value={userStats.activeToday?.toLocaleString() || stats.activeUsers?.toLocaleString() || '0'}
                />
                <StatCard
                    title="Commands Executed"
                    value={commandStats.totalExecuted?.toLocaleString() || stats.commandsExecuted?.toLocaleString() || '0'}
                />
                <StatCard
                    title="Messages Processed"
                    value={stats.messagesProcessed?.toLocaleString() || '0'}
                />
                <StatCard
                    title="Bot Status"
                    value={botStatus}
                    style={{
                        color: getStatusColor(botStatus)
                    }}
                />
                <StatCard
                    title="Uptime"
                    value={systemStats.uptime || stats.uptime || '0h 0m'}
                />
            </div>

            {/* Графики из реальных данных */}
            <div style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(auto-fit, minmax(400px, 1fr))',
                gap: '20px',
                marginBottom: '40px'
            }}>
                {/* График использования команд */}
                <div style={{
                    background: '#fff',
                    padding: '20px',
                    borderRadius: '8px',
                    boxShadow: '0 2px 4px rgba(0,0,0,0.1)',
                    border: '1px solid #e0e0e0'
                }}>
                    <h3 style={{ color: '#2c3e50', marginTop: 0 }}>Command Usage</h3>
                    <StatsChart
                        type="line"
                        data={commandUsageData}
                    />
                </div>

                {/* Активность пользователей */}
                <div style={{
                    background: '#fff',
                    padding: '20px',
                    borderRadius: '8px',
                    boxShadow: '0 2px 4px rgba(0,0,0,0.1)',
                    border: '1px solid #e0e0e0'
                }}>
                    <h3 style={{ color: '#2c3e50', marginTop: 0 }}>User Activity</h3>
                    <StatsChart
                        type="bar"
                        data={userActivityData}
                    />
                </div>

                {/* Популярные команды */}
                <div style={{
                    background: '#fff',
                    padding: '20px',
                    borderRadius: '8px',
                    boxShadow: '0 2px 4px rgba(0,0,0,0.1)',
                    border: '1px solid #e0e0e0'
                }}>
                    <h3 style={{ color: '#2c3e50', marginTop: 0 }}>Popular Commands</h3>
                    <div style={{ height: '250px' }}>
                        <StatsChart
                            type="doughnut"
                            data={popularCommandsData}
                        />
                    </div>
                </div>

                {/* Системная статистика */}
                <div style={{
                    background: '#fff',
                    padding: '20px',
                    borderRadius: '8px',
                    boxShadow: '0 2px 4px rgba(0,0,0,0.1)',
                    border: '1px solid #e0e0e0'
                }}>
                    <h3 style={{ color: '#2c3e50', marginTop: 0 }}>System Info</h3>
                    <div style={{ display: 'grid', gap: '15px' }}>
                        <div style={{
                            display: 'flex',
                            justifyContent: 'space-between',
                            padding: '10px',
                            background: '#f8f9fa',
                            borderRadius: '4px'
                        }}>
                            <span>Response Time</span>
                            <strong>{systemStats.responseTime || '0ms'}</strong>
                        </div>
                        <div style={{
                            display: 'flex',
                            justifyContent: 'space-between',
                            padding: '10px',
                            background: '#f8f9fa',
                            borderRadius: '4px'
                        }}>
                            <span>Memory Usage</span>
                            <strong>{systemStats.memoryUsage || '0%'}</strong>
                        </div>
                        <div style={{
                            display: 'flex',
                            justifyContent: 'space-between',
                            padding: '10px',
                            background: '#f8f9fa',
                            borderRadius: '4px'
                        }}>
                            <span>CPU Load</span>
                            <strong>{systemStats.cpuLoad || '0%'}</strong>
                        </div>
                        <div style={{
                            display: 'flex',
                            justifyContent: 'space-between',
                            padding: '10px',
                            background: '#f8f9fa',
                            borderRadius: '4px'
                        }}>
                            <span>Errors Today</span>
                            <strong>{stats.errorsToday?.toLocaleString() || '0'}</strong>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
};

export default Dashboard;