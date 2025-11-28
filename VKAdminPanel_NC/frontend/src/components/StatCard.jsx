import React from 'react';

export const StatCard = ({ title, value, trend, style }) => (
    <div className="stat-card" style={{
        padding: '20px',
        borderRadius: '8px',
        textAlign: 'center',
        minHeight: '100px',
        display: 'flex',
        flexDirection: 'column',
        justifyContent: 'center'
    }}>
        <h3 style={{
            margin: '0 0 8px 0',
            fontSize: '28px',
            fontWeight: 'bold',
            color: style?.color || 'var(--text-primary)'
        }}>
            {value}
        </h3>
        <p style={{
            margin: '0',
            color: 'var(--text-secondary)',
            fontSize: '14px',
            fontWeight: '500'
        }}>
            {title}
        </p>
        {trend && (
            <span style={{
                fontSize: '12px',
                color: trend.startsWith('+') ? '#27ae60' : '#e74c3c',
                fontWeight: 'bold',
                marginTop: '5px'
            }}>
                {trend}
            </span>
        )}
    </div>
);