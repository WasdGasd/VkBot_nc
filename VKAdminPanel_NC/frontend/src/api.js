const API_BASE = 'http://localhost:5101/api';

export const api = {
    // Статистика
    getStats: () => fetch(`${API_BASE}/stats/bot`).then(r => r.json()),
    getSystemStats: () => fetch(`${API_BASE}/stats/system`).then(r => r.json()),
    simulateActivity: () => fetch(`${API_BASE}/stats/simulate`, { method: 'POST' }),

    // Управление ботом
    getBotStatus: () => fetch(`${API_BASE}/bot/status`).then(r => r.json()),
    enableBot: () => fetch(`${API_BASE}/bot/enable`, { method: 'POST' }),
    disableBot: () => fetch(`${API_BASE}/bot/disable`, { method: 'POST' }),
    restartBot: () => fetch(`${API_BASE}/bot/restart`, { method: 'POST' }),

    // Команды
    getCommands: () => fetch(`${API_BASE}/commands`).then(r => r.json()),
    createCommand: (command) => fetch(`${API_BASE}/commands`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(command)
    }),
    updateCommand: (id, command) => fetch(`${API_BASE}/commands/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(command)
    }),
    deleteCommand: (id) => fetch(`${API_BASE}/commands/${id}`, { method: 'DELETE' }),

    // Логи
    getLogs: () => fetch(`${API_BASE}/logs`).then(r => r.json())
};

// Алиасы для совместимости с Dashboard.jsx
export const getStats = api.getStats;
export const getBotStatus = api.getBotStatus;
export const startBot = api.enableBot;
export const stopBot = api.disableBot;
export const restartBot = api.restartBot;
export const getCommandStats = () => fetch(`${API_BASE}/stats/commands`).then(r => r.json());
export const getUserStats = () => fetch(`${API_BASE}/stats/users`).then(r => r.json());
export const getSystemStats = api.getSystemStats;
export const fetchCommands = api.getCommands;
export const createCommand = api.createCommand;
export const updateCommand = api.updateCommand;
export const deleteCommand = api.deleteCommand;