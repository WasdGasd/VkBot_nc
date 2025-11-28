import React from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { useTheme } from '../contexts/ThemeContext';

const Sidebar = () => {
    const location = useLocation();
    const { user, logout } = useAuth();
    const navigate = useNavigate();
    const { isDark, toggleTheme } = useTheme();

    const menuItems = [
        { path: '/', label: 'Dashboard', icon: '📊' },
        { path: '/commands', label: 'Commands', icon: '⚙️' },
        { path: '/logs', label: 'Logs', icon: '📝' }
    ];

    const handleLogout = () => {
        logout();
        navigate('/login');
    };

    return (
        <div style={{
            width: '250px',
            background: '#2c3e50',
            color: 'white',
            height: '100vh',
            position: 'fixed',
            left: 0,
            top: 0,
            padding: '20px 0',
            display: 'flex',
            flexDirection: 'column'
        }}>
            <div style={{
                padding: '0 20px 20px',
                borderBottom: '1px solid #34495e',
                marginBottom: '20px'
            }}>
                <h2 style={{ margin: 0, color: '#ecf0f1' }}>VK Bot Admin</h2>
                <p style={{ margin: '5px 0 0', color: '#bdc3c7', fontSize: '12px' }}>
                    Welcome, {user?.username}
                </p>
            </div>

            <nav style={{ flex: 1 }}>
                {menuItems.map(item => (
                    <Link
                        key={item.path}
                        to={item.path}
                        style={{
                            display: 'flex',
                            alignItems: 'center',
                            padding: '12px 20px',
                            color: location.pathname === item.path ? '#3498db' : '#bdc3c7',
                            textDecoration: 'none',
                            background: location.pathname === item.path ? '#34495e' : 'transparent',
                            borderLeft: location.pathname === item.path ? '4px solid #3498db' : '4px solid transparent'
                        }}
                    >
                        <span style={{ marginRight: '10px', fontSize: '16px' }}>
                            {item.icon}
                        </span>
                        {item.label}
                    </Link>
                ))}
            </nav>

            <div style={{ padding: '20px', borderTop: '1px solid #34495e' }}>
                <button
                    onClick={toggleTheme}
                    style={{
                        width: '100%',
                        background: 'transparent',
                        color: '#bdc3c7',
                        border: '1px solid #34495e',
                        padding: '10px',
                        borderRadius: '4px',
                        cursor: 'pointer',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        marginBottom: '10px'
                    }}
                >
                    <span style={{ marginRight: '8px' }}>
                        {isDark ? '☀️' : '🌙'}
                    </span>
                    {isDark ? 'Light Mode' : 'Dark Mode'}
                </button>

                <button
                    onClick={handleLogout}
                    style={{
                        width: '100%',
                        background: 'transparent',
                        color: '#bdc3c7',
                        border: '1px solid #34495e',
                        padding: '10px',
                        borderRadius: '4px',
                        cursor: 'pointer',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center'
                    }}
                >
                    <span style={{ marginRight: '8px' }}>🚪</span>
                    Logout
                </button>
            </div>
        </div>
    );
};

export default Sidebar;