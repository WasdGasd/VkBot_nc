import React, { useState, useEffect } from 'react';
import { useNotification } from '../contexts/NotificationContext';
import CommandForm from "../components/CommandForm";
import { CommandList } from "../components/CommandList";
import { fetchCommands, createCommand, updateCommand, deleteCommand } from '../api';

export default function CommandsPage() {
    const [commands, setCommands] = useState([]);
    const [loading, setLoading] = useState(true);
    const [editingCommand, setEditingCommand] = useState(null);
    const { addNotification } = useNotification();

    useEffect(() => {
        loadCommands();
    }, []);

    const loadCommands = async () => {
        try {
            const commandsData = await fetchCommands();
            setCommands(commandsData);
        } catch (error) {
            console.error('Error loading commands:', error);
            addNotification('Failed to load commands', 'error');
            // Заглушка для демонстрации
            setCommands([
                {
                    id: 1,
                    name: 'start',
                    description: 'Start the bot',
                    response: 'Bot started!',
                    isActive: true
                },
                {
                    id: 2,
                    name: 'help',
                    description: 'Show help information',
                    response: 'Available commands: ...',
                    isActive: true
                },
                {
                    id: 3,
                    name: 'status',
                    description: 'Check bot status',
                    response: 'Bot is running normally',
                    isActive: false
                }
            ]);
        } finally {
            setLoading(false);
        }
    };

    const handleSave = async (commandData) => {
        try {
            if (editingCommand) {
                await updateCommand(editingCommand.id, commandData);
                addNotification('Command updated successfully!', 'success');
            } else {
                await createCommand(commandData);
                addNotification('Command created successfully!', 'success');
            }

            await loadCommands();
            setEditingCommand(null);
        } catch (error) {
            console.error('Error saving command:', error);
            addNotification('Failed to save command', 'error');
        }
    };

    const handleEdit = (command) => {
        setEditingCommand(command);
    };

    const handleDelete = async (commandId) => {
        if (window.confirm('Are you sure you want to delete this command?')) {
            try {
                await deleteCommand(commandId);
                addNotification('Command deleted successfully!', 'success');
                await loadCommands();
            } catch (error) {
                console.error('Error deleting command:', error);
                addNotification('Failed to delete command', 'error');
            }
        }
    };

    const handleCancelEdit = () => {
        setEditingCommand(null);
    };

    if (loading) {
        return (
            <div style={{ padding: '20px' }}>
                <div>Loading commands...</div>
            </div>
        );
    }

    return (
        <div style={{
            padding: '20px',
            maxWidth: '1200px',
            margin: '0 auto'
        }}>
            <div style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                marginBottom: '30px',
                flexWrap: 'wrap',
                gap: '20px'
            }}>
                <h1 style={{
                    color: '#2c3e50',
                    margin: 0,
                    borderBottom: '2px solid #ecf0f1',
                    paddingBottom: '10px'
                }}>
                    Bot Commands Management
                </h1>

                {!editingCommand && (
                    <button
                        onClick={() => setEditingCommand({})}
                        style={{
                            background: '#3498db',
                            color: 'white',
                            border: 'none',
                            padding: '10px 20px',
                            borderRadius: '4px',
                            cursor: 'pointer',
                            fontWeight: 'bold'
                        }}
                    >
                        + Add New Command
                    </button>
                )}
            </div>

            {editingCommand && (
                <div style={{
                    background: '#fff',
                    padding: '25px',
                    borderRadius: '8px',
                    boxShadow: '0 2px 10px rgba(0,0,0,0.1)',
                    marginBottom: '30px',
                    border: '1px solid #e0e0e0'
                }}>
                    <h2 style={{
                        color: '#2c3e50',
                        marginTop: 0,
                        marginBottom: '20px'
                    }}>
                        {editingCommand.id ? 'Edit Command' : 'Add New Command'}
                    </h2>
                    <CommandForm
                        command={editingCommand}
                        onSave={handleSave}
                        onCancel={handleCancelEdit}
                    />
                </div>
            )}

            <div style={{
                background: '#fff',
                borderRadius: '8px',
                boxShadow: '0 2px 4px rgba(0,0,0,0.1)',
                border: '1px solid #e0e0e0',
                overflow: 'hidden'
            }}>
                <CommandList
                    commands={commands}
                    onEdit={handleEdit}
                    onDelete={handleDelete}
                />
            </div>

            <div style={{
                marginTop: '20px',
                color: '#7f8c8d',
                fontSize: '14px',
                textAlign: 'center'
            }}>
                Total commands: {commands.length}
                {commands.filter(cmd => cmd.isActive).length > 0 &&
                    ` (${commands.filter(cmd => cmd.isActive).length} active)`
                }
            </div>
        </div>
    );
}