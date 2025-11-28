import React, { createContext, useState, useContext, useEffect } from 'react';
import { login as apiLogin } from '../api';

const AuthContext = createContext();

export const useAuth = () => {
    const context = useContext(AuthContext);
    if (!context) {
        throw new Error('useAuth must be used within an AuthProvider');
    }
    return context;
};

export const AuthProvider = ({ children }) => {
    const [user, setUser] = useState(null);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        // Проверяем, есть ли сохраненная сессия
        const token = localStorage.getItem('token');
        if (token) {
            setUser({ username: 'admin' }); // Заглушка
        }
        setLoading(false);
    }, []);

    // Функция логина - ОДНО объявление!
    const login = async (username, password) => {
        try {
            // Временная заглушка - удали это когда API заработает
            if (username === 'admin' && password === 'admin') {
                localStorage.setItem('token', 'demo-token');
                setUser({ username: 'admin' });
                return { success: true };
            }
            return { success: false, error: 'Invalid credentials' };

            
            const response = await apiLogin(username, password);
            localStorage.setItem('token', response.token);
            setUser({ username });
            return { success: true };
            
        } catch (error) {
            return { success: false, error: error.message };
        }
    };

    const logout = () => {
        localStorage.removeItem('token');
        setUser(null);
    };

    const value = {
        user,
        login,
        logout,
        isAuthenticated: !!user
    };

    return (
        <AuthContext.Provider value={value}>
            {children}
        </AuthContext.Provider>
    );
};