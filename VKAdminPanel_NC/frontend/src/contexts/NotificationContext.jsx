import React, { createContext, useState, useContext, useCallback } from 'react';

const NotificationContext = createContext();

export const useNotification = () => {
    const context = useContext(NotificationContext);
    if (!context) {
        throw new Error('useNotification must be used within a NotificationProvider');
    }
    return context;
};

export const NotificationProvider = ({ children }) => {
    const [notifications, setNotifications] = useState([]);

    const addNotification = useCallback((message, type = 'info', duration = 5000) => {
        const id = Date.now() + Math.random();
        const notification = { id, message, type, duration };

        setNotifications(prev => [...prev, notification]);

        if (duration > 0) {
            setTimeout(() => {
                removeNotification(id);
            }, duration);
        }

        return id;
    }, []);

    const removeNotification = useCallback((id) => {
        setNotifications(prev => prev.filter(notification => notification.id !== id));
    }, []);

    const clearAll = useCallback(() => {
        setNotifications([]);
    }, []);

    const value = {
        notifications,
        addNotification,
        removeNotification,
        clearAll
    };

    return (
        <NotificationContext.Provider value={value}>
            {children}
            <NotificationContainer />
        </NotificationContext.Provider>
    );
};

// Компонент для отображения уведомлений
const NotificationContainer = () => {
    const { notifications, removeNotification } = useNotification();

    if (notifications.length === 0) return null;

    return (
        <div style={{
            position: 'fixed',
            top: '20px',
            right: '20px',
            zIndex: 1000,
            maxWidth: '400px'
        }}>
            {notifications.map(notification => (
                <Notification
                    key={notification.id}
                    notification={notification}
                    onClose={() => removeNotification(notification.id)}
                />
            ))}
        </div>
    );
};

const Notification = ({ notification, onClose }) => {
    const { type, message } = notification;

    const getNotificationStyle = (type) => {
        const baseStyle = {
            padding: '15px 20px',
            marginBottom: '10px',
            borderRadius: '8px',
            boxShadow: '0 4px 6px rgba(0, 0, 0, 0.1)',
            color: 'white',
            display: 'flex',
            alignItems: 'flex-start',
            gap: '10px',
            maxWidth: '400px',
            animation: 'slideIn 0.3s ease-out'
        };

        const styles = {
            info: { ...baseStyle, background: '#3498db' },
            success: { ...baseStyle, background: '#27ae60' },
            warning: { ...baseStyle, background: '#f39c12' },
            error: { ...baseStyle, background: '#e74c3c' }
        };

        return styles[type] || styles.info;
    };

    const getIcon = (type) => {
        switch (type) {
            case 'success': return '✅';
            case 'warning': return '⚠️';
            case 'error': return '❌';
            default: return 'ℹ️';
        }
    };

    return (
        <div style={getNotificationStyle(type)}>
            <span style={{ fontSize: '16px' }}>{getIcon(type)}</span>
            <div style={{ flex: 1 }}>
                {message}
            </div>
            <button
                onClick={onClose}
                style={{
                    background: 'transparent',
                    border: 'none',
                    color: 'white',
                    fontSize: '18px',
                    cursor: 'pointer',
                    padding: '0',
                    marginLeft: '10px'
                }}
            >
                ×
            </button>
        </div>
    );
};

// Добавь эти стили в твой index.css
const notificationStyles = `
@keyframes slideIn {
    from {
        transform: translateX(100%);
        opacity: 0;
    }
    to {
        transform: translateX(0);
        opacity: 1;
    }
}
`;