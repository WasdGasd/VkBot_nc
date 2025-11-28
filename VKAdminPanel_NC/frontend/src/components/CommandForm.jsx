import React, { useState, useEffect } from 'react';

const CommandForm = ({ command, onSave, onCancel }) => {
    const [formData, setFormData] = useState({
        name: '',
        description: '',
        response: '',
        isActive: true
    });

    useEffect(() => {
        if (command) {
            setFormData({
                name: command.name || '',
                description: command.description || '',
                response: command.response || '',
                isActive: command.isActive !== undefined ? command.isActive : true
            });
        }
    }, [command]);

    const handleSubmit = (e) => {
        e.preventDefault();
        if (!formData.name.trim() || !formData.response.trim()) {
            alert('Please fill in all required fields');
            return;
        }
        onSave(formData);
    };

    const handleChange = (e) => {
        const { name, value, type, checked } = e.target;
        setFormData(prev => ({
            ...prev,
            [name]: type === 'checkbox' ? checked : value
        }));
    };

    return (
        <form onSubmit={handleSubmit}>
            <div style={{
                display: 'grid',
                gridTemplateColumns: '1fr 1fr',
                gap: '20px',
                marginBottom: '20px'
            }}>
                <div>
                    <label style={{
                        display: 'block',
                        marginBottom: '8px',
                        fontWeight: 'bold',
                        color: '#2c3e50'
                    }}>
                        Command Name *
                    </label>
                    <input
                        type="text"
                        name="name"
                        value={formData.name}
                        onChange={handleChange}
                        placeholder="e.g., 'help', 'start'"
                        style={{
                            width: '100%',
                            padding: '10px',
                            border: '1px solid #ddd',
                            borderRadius: '4px',
                            fontSize: '14px'
                        }}
                        required
                    />
                </div>

                <div>
                    <label style={{
                        display: 'block',
                        marginBottom: '8px',
                        fontWeight: 'bold',
                        color: '#2c3e50'
                    }}>
                        Description *
                    </label>
                    <input
                        type="text"
                        name="description"
                        value={formData.description}
                        onChange={handleChange}
                        placeholder="What does this command do?"
                        style={{
                            width: '100%',
                            padding: '10px',
                            border: '1px solid #ddd',
                            borderRadius: '4px',
                            fontSize: '14px'
                        }}
                        required
                    />
                </div>
            </div>

            <div style={{ marginBottom: '20px' }}>
                <label style={{
                    display: 'block',
                    marginBottom: '8px',
                    fontWeight: 'bold',
                    color: '#2c3e50'
                }}>
                    Bot Response *
                </label>
                <textarea
                    name="response"
                    value={formData.response}
                    onChange={handleChange}
                    placeholder="What should the bot reply when this command is used?"
                    style={{
                        width: '100%',
                        padding: '10px',
                        border: '1px solid #ddd',
                        borderRadius: '4px',
                        fontSize: '14px',
                        minHeight: '100px',
                        resize: 'vertical'
                    }}
                    required
                />
            </div>

            <div style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                marginBottom: '20px'
            }}>
                <label style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '8px',
                    cursor: 'pointer'
                }}>
                    <input
                        type="checkbox"
                        name="isActive"
                        checked={formData.isActive}
                        onChange={handleChange}
                    />
                    <span style={{ fontWeight: 'bold', color: '#2c3e50' }}>
                        Active
                    </span>
                </label>
            </div>

            <div style={{
                display: 'flex',
                gap: '10px',
                justifyContent: 'flex-end'
            }}>
                <button
                    type="button"
                    onClick={onCancel}
                    style={{
                        background: '#95a5a6',
                        color: 'white',
                        border: 'none',
                        padding: '10px 20px',
                        borderRadius: '4px',
                        cursor: 'pointer',
                        fontSize: '14px'
                    }}
                >
                    Cancel
                </button>
                <button
                    type="submit"
                    style={{
                        background: '#27ae60',
                        color: 'white',
                        border: 'none',
                        padding: '10px 20px',
                        borderRadius: '4px',
                        cursor: 'pointer',
                        fontSize: '14px',
                        fontWeight: 'bold'
                    }}
                >
                    {command?.id ? 'Update Command' : 'Create Command'}
                </button>
            </div>
        </form>
    );
};

export default CommandForm;