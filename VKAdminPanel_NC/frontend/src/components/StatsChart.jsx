import React from 'react';
import {
    Chart as ChartJS,
    CategoryScale,
    LinearScale,
    PointElement,
    LineElement,
    BarElement,
    Title,
    Tooltip,
    Legend,
    ArcElement
} from 'chart.js';
import { Line, Bar, Doughnut } from 'react-chartjs-2';

ChartJS.register(
    CategoryScale,
    LinearScale,
    PointElement,
    LineElement,
    BarElement,
    Title,
    Tooltip,
    Legend,
    ArcElement
);

export const StatsChart = ({ type, data, options }) => {
    const chartProps = {
        data,
        options: {
            responsive: true,
            plugins: {
                legend: {
                    position: 'top',
                },
            },
            ...options,
        },
    };

    switch (type) {
        case 'line':
            return <Line {...chartProps} />;
        case 'bar':
            return <Bar {...chartProps} />;
        case 'doughnut':
            return <Doughnut {...chartProps} />;
        default:
            return <Bar {...chartProps} />;
    }
};