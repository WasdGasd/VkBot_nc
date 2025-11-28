import React from 'react';

export const CommandList = ({ commands = [], onEdit, onDelete }) => {
    if (!commands || commands.length === 0) {
        return (
            <div style={{
                padding: '40px',
                textAlign: 'center',
                color: '#7f8c8d'
            }}>
                No commands found. Create your first command!
            </div>
        );
    }

    return (
        <div>
            <div style={{
                padding: '15px 20px',
                background: '#f8f9fa',
                borderBottom: '1px solid #e0e0e0',
                fontWeight: 'bold',
                color: '#2c3e50'
            }}>
                Available Commands
            </div>

            <div style={{ maxHeight: '500px', overflowY: 'auto' }}>
                {commands.map((command, index) => (
                    <div
                        key={command.id || index}
                        style={{
                            padding: '15px 20px',
                            borderBottom: '1px solid #f0f0f0',
                            display: 'flex',
                            justifyContent: 'space-between',
                            alignItems: 'flex-start',
                            gap: '15px',
                            background: index % 2 === 0 ? '#fafafa' : 'white'
                        }}
                    >
                        <div style={{ flex: 1 }}>
                            <div style={{
                                display: 'flex',
                                alignItems: 'center',
                                gap: '10px',
                                marginBottom: '8px'
                            }}>
                                <span style={{
                                    background: command.isActive ? '#27ae60' : '#95a5a6',
                                    color: 'white',
                                    padding: '2px 8px',
                                    borderRadius: '12px',
                                    fontSize: '12px',
                                    fontWeight: 'bold'
                                }}>
                                    {command.isActive ? 'ACTIVE' : 'INACTIVE'}
                                </span>
                                <strong style={{
                                    color: '#2c3e50',
                                    fontSize: '16px'
                                }}>
                                    /{command.name}
                                </strong>
                            </div>

                            <div style={{
                                color: '#7f8c8d',
                                marginBottom: '8px',
                                fontSize: '14px'
                            }}>
                                {command.description}
                            </div>

                            <div style={{
                                background: '#f8f9fa',
                                padding: '8px 12px',
                                borderRadius: '4px',
                                fontSize: '13px',
                                color: '#2c3e50',
                                borderLeft: '3px solid #3498db'
                            }}>
                                <strong>Response:</strong> {command.response}
                            </div>
                        </div>

                        <div style={{
                            display: 'flex',
                            gap: '8px',
                            flexShrink: 0
                        }}>
                            <button
                                onClick={() => onEdit(command)}
                                style={{
                                    background: '#3498db',
                                    color: 'white',
                                    border: 'none',
                                    padding: '6px 12px',
                                    borderRadius: '4px',
                                    cursor: 'pointer',
                                    fontSize: '12px'
                                }}
                            >
                                Edit
                            </button>
                            <button
                                onClick={() => onDelete(command.id)}
                                style={{
                                    background: '#e74c3c',
                                    color: 'white',
                                    border: 'none',
                                    padding: '6px 12px',
                                    borderRadius: '4px',
                                    cursor: 'pointer',
                                    fontSize: '12px'
                                }}
                            >
                                Delete
                            </button>
                        </div>
                    </div>
                ))}
            </div>
        </div>
    );
};