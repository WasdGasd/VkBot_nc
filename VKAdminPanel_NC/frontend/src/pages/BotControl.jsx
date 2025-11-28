import React from 'react';
import { botControl } from '../api';

export default function BotControl() {
    const handleAction = async action => {
        await botControl(action);
        alert(`Bot ${action} executed`);
    };

    return (
        <div>
            <h2>Bot Control</h2>
            <button onClick={() => handleAction('start')}>Start Bot</button>
            <button onClick={() => handleAction('stop')}>Stop Bot</button>
            <button onClick={() => handleAction('reload')}>Reload Commands</button>
        </div>
    );
}
