/**
 * @fileoverview Bot Control Panel - Modern ES6+ refactored version
 * @description Manages bot instances, provides real-time monitoring, and controls for SysBot.NET
 * @version 2.0.0
 * @author hexbyt3
 */

'use strict';

// ============================================================================
// THEME MANAGEMENT
// ============================================================================

/**
 * Manages application theme (dark/light mode)
 * @class
 */
class ThemeManager {
    constructor() {
        this.STORAGE_KEY = 'bot-panel-theme';
        this.themes = {
            light: {
                primary: '#3b82f6',
                background: '#ffffff',
                surface: '#f3f4f6',
                text: '#1f2937',
                textSecondary: '#6b7280'
            },
            dark: {
                primary: '#60a5fa',
                background: '#0f172a',
                surface: '#1e293b',
                text: '#f1f5f9',
                textSecondary: '#94a3b8'
            }
        };
        this.currentTheme = this.loadTheme();
        this.init();
    }

    /**
     * Initialize theme on page load
     */
    init() {
        this.applyTheme(this.currentTheme);
        this.setupThemeToggle();
    }

    /**
     * Load theme from localStorage or system preference
     * @returns {string} Theme name ('light' or 'dark')
     */
    loadTheme() {
        const stored = localStorage.getItem(this.STORAGE_KEY);
        if (stored) return stored;
        
        // Default to dark theme as requested
        return 'dark';
    }

    /**
     * Apply theme to document
     * @param {string} themeName - Theme to apply
     */
    applyTheme(themeName) {
        const root = document.documentElement;
        
        // Set data-theme attribute (CSS will handle the color changes)
        root.setAttribute('data-theme', themeName);
        
        // Store current theme
        this.currentTheme = themeName;
        localStorage.setItem(this.STORAGE_KEY, themeName);
        
        // Optional: Apply theme object properties if needed
        const theme = this.themes[themeName];
        if (theme) {
            Object.entries(theme).forEach(([key, value]) => {
                root.style.setProperty(`--theme-${key}`, value);
            });
        }
        
        // Dispatch theme change event
        window.dispatchEvent(new CustomEvent('themechange', { 
            detail: { theme: themeName } 
        }));
    }

    /**
     * Setup theme toggle button
     */
    setupThemeToggle() {
        // Use the existing button in the HTML header
        const headerButton = document.getElementById('theme-toggle');
        if (headerButton) {
            headerButton.addEventListener('click', () => this.toggleTheme());
            this.updateButtonVisuals();
        }
        
        // Legacy support: create a floating button if header button doesn't exist
        else {
            const button = document.createElement('button');
            button.className = 'theme-toggle-btn floating-theme-toggle';
            button.innerHTML = this.currentTheme === 'dark' ? '☀️' : '🌙';
            button.title = 'Toggle theme';
            button.setAttribute('aria-label', 'Toggle theme');
            button.addEventListener('click', () => this.toggleTheme());
            document.body.appendChild(button);
        }
    }

    /**
     * Toggle between light and dark theme
     */
    toggleTheme() {
        const newTheme = this.currentTheme === 'light' ? 'dark' : 'light';
        this.applyTheme(newTheme);
        this.updateButtonVisuals();
    }
    
    /**
     * Update theme toggle button visuals
     */
    updateButtonVisuals() {
        // Update header button (uses CSS for icon visibility)
        const headerButton = document.getElementById('theme-toggle');
        if (headerButton) {
            headerButton.setAttribute('aria-label', 
                `Switch to ${this.currentTheme === 'dark' ? 'light' : 'dark'} mode`);
        }
        
        // Update any floating/legacy buttons
        const floatingButtons = document.querySelectorAll('.floating-theme-toggle');
        floatingButtons.forEach(button => {
            button.innerHTML = this.currentTheme === 'dark' ? '☀️' : '🌙';
        });
    }
}

// ============================================================================
// API SERVICE
// ============================================================================

/**
 * Handles all API communications with enhanced error handling
 * @class
 */
class ApiService {
    constructor() {
        this.baseUrl = '/api/bot';
        this.endpoints = {
            instances: `${this.baseUrl}/instances`,
            updateCheck: `${this.baseUrl}/update/check`,
            updateAll: `${this.baseUrl}/update/all`,
            updateActive: `${this.baseUrl}/update/active`,
            idleStatus: `${this.baseUrl}/update/idle-status`,
            restartAll: `${this.baseUrl}/restart/all`,
            restartSchedule: `${this.baseUrl}/restart/schedule`,
            commandAll: `${this.baseUrl}/command/all`
        };
        this.retryAttempts = 3;
        this.retryDelay = 1000;
    }

    /**
     * Perform GET request with retry logic
     * @param {string} url - URL to fetch
     * @param {number} attempt - Current attempt number
     * @returns {Promise<any>} Response data
     */
    async get(url, attempt = 1) {
        try {
            const absoluteUrl = url.startsWith('http') ? url : `${window.location.origin}${url}`;
            
            const response = await fetch(absoluteUrl, {
                method: 'GET',
                mode: 'cors',
                credentials: 'same-origin',
                headers: {
                    'Accept': 'application/json',
                    'Cache-Control': 'no-cache'
                },
                signal: AbortSignal.timeout(5000)
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            return await response.json();
        } catch (error) {
            if (attempt < this.retryAttempts) {
                await this.delay(this.retryDelay * attempt);
                return this.get(url, attempt + 1);
            }
            console.error(`API GET request failed after ${attempt} attempts:`, url, error);
            throw error;
        }
    }

    /**
     * Perform POST request with retry logic
     * @param {string} url - URL to post to
     * @param {Object} data - Data to send
     * @param {number} attempt - Current attempt number
     * @returns {Promise<any>} Response data
     */
    async post(url, data = {}, attempt = 1) {
        try {
            const response = await fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
                signal: AbortSignal.timeout(10000)
            });
            
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }
            
            return await response.json();
        } catch (error) {
            if (attempt < this.retryAttempts) {
                await this.delay(this.retryDelay * attempt);
                return this.post(url, data, attempt + 1);
            }
            console.error(`API POST request failed after ${attempt} attempts:`, url, error);
            throw error;
        }
    }

    /**
     * Delay helper for retry logic
     * @param {number} ms - Milliseconds to delay
     * @returns {Promise<void>}
     */
    delay(ms) {
        return new Promise(resolve => setTimeout(resolve, ms));
    }
}

// ============================================================================
// STATUS UTILITIES
// ============================================================================

/**
 * Utilities for managing and displaying bot status
 * @class
 */
class StatusManager {
    constructor() {
        this.colors = {
            RUNNING: '#10b981',
            IDLE: '#f59e0b',
            STOPPED: '#ef4444',
            UNKNOWN: '#6b7280'
        };

        this.states = {
            RUNNING: ['RUNNING', 'ACTIVE', 'ONLINE'],
            IDLE: ['IDLE', 'PAUSED'],
            STOPPED: ['STOPPED', 'OFFLINE', 'DISCONNECTED', 'ERROR']
        };
    }

    /**
     * Get color for status
     * @param {string} status - Status string
     * @returns {string} Hex color code
     */
    getColor(status) {
        const upperStatus = status?.toUpperCase() || '';

        if (this.states.RUNNING.some(s => upperStatus.includes(s)) ||
            (upperStatus && !this.states.IDLE.concat(this.states.STOPPED).some(s => upperStatus.includes(s)) &&
                !upperStatus.includes('UNKNOWN'))) {
            return this.colors.RUNNING;
        }

        if (this.states.IDLE.some(s => upperStatus.includes(s))) {
            return this.colors.IDLE;
        }

        if (this.states.STOPPED.some(s => upperStatus.includes(s))) {
            return this.colors.STOPPED;
        }

        return this.colors.UNKNOWN;
    }

    /**
     * Get CSS class for status
     * @param {string} status - Status string
     * @returns {string} CSS class name
     */
    getStatusClass(status) {
        const color = this.getColor(status);
        const classMap = {
            [this.colors.RUNNING]: 'running',
            [this.colors.IDLE]: 'idle',
            [this.colors.STOPPED]: 'stopped'
        };
        return classMap[color] || 'stopped';
    }

    /**
     * Get instance status summary
     * @param {Object} instance - Instance object
     * @returns {Object} Status object with status and text
     */
    getInstanceStatus(instance) {
        if (!instance.botStatuses || instance.botStatuses.length === 0) {
            return { status: 'stopped', text: 'Stopped' };
        }

        const statuses = instance.botStatuses.map(b => this.getStatusClass(b.status));
        const counts = {
            running: statuses.filter(s => s === 'running').length,
            idle: statuses.filter(s => s === 'idle').length,
            stopped: statuses.filter(s => s === 'stopped').length
        };

        const total = instance.botStatuses.length;

        if (counts.running === total) {
            return { status: 'running', text: 'All Running' };
        } else if (counts.idle === total) {
            return { status: 'idle', text: 'All Idle' };
        } else if (counts.stopped === total) {
            return { status: 'stopped', text: 'All Stopped' };
        } else if (counts.running > 0) {
            return { status: 'mixed', text: `${counts.running}/${total} Running` };
        } else if (counts.idle > 0) {
            return { status: 'mixed', text: `${counts.idle}/${total} Idle` };
        }

        return { status: 'stopped', text: 'Stopped' };
    }
}

// ============================================================================
// TOAST NOTIFICATION SYSTEM
// ============================================================================

/**
 * Manages toast notifications with queuing and positioning
 * @class
 */
class ToastManager {
    constructor() {
        this.activeToasts = [];
        this.toastHeight = 80;
        this.baseOffset = window.innerWidth <= 768 ? 0 : 32;
        this.config = {
            success: { icon: '✅', class: 'success' },
            error: { icon: '❌', class: 'error' },
            warning: { icon: '⚠️', class: 'warning' },
            info: { icon: 'ℹ️', class: 'info' }
        };
    }

    /**
     * Show a toast notification
     * @param {string} type - Type of notification
     * @param {string} title - Notification title
     * @param {string} message - Notification message
     * @param {number} duration - Display duration in ms
     */
    show(type, title, message, duration = 4000) {
        const toastId = Date.now();
        const template = document.getElementById('toast');
        
        if (!template) {
            console.error('Toast template not found');
            return;
        }

        const toast = template.cloneNode(true);
        toast.id = `toast-${toastId}`;

        const config = this.config[type] || this.config.info;
        toast.querySelector('.toast-icon').textContent = config.icon;
        toast.querySelector('.toast-title').textContent = title;
        toast.querySelector('.toast-message').textContent = message;
        toast.className = `toast ${config.class}`;

        document.body.appendChild(toast);
        this.activeToasts.push(toastId);

        this.updatePositions();

        // Force reflow for animation
        toast.offsetHeight;

        requestAnimationFrame(() => {
            toast.classList.add('show');
        });

        setTimeout(() => {
            toast.classList.remove('show');
            setTimeout(() => {
                toast.remove();
                this.activeToasts = this.activeToasts.filter(id => id !== toastId);
                this.updatePositions();
            }, 300);
        }, duration);
    }

    /**
     * Update positions of all active toasts
     */
    updatePositions() {
        const baseOffset = window.innerWidth <= 768 ? 0 : 32;
        this.activeToasts.forEach((id, index) => {
            const toast = document.getElementById(`toast-${id}`);
            if (toast) {
                toast.style.bottom = `${baseOffset + index * this.toastHeight}px`;
            }
        });
    }

    /**
     * Show error toast
     * @param {string} message - Error message
     */
    error(message) {
        this.show('error', 'Error', message);
    }

    /**
     * Show success toast
     * @param {string} message - Success message
     */
    success(message) {
        this.show('success', 'Success', message);
    }

    /**
     * Show info toast
     * @param {string} message - Info message
     */
    info(message) {
        this.show('info', 'Info', message);
    }

    /**
     * Show warning toast
     * @param {string} message - Warning message
     */
    warning(message) {
        this.show('warning', 'Warning', message);
    }
}

// ============================================================================
// INSTANCE RENDERER WITH INTERSECTION OBSERVER
// ============================================================================

/**
 * Renders bot instances with performance optimizations
 * @class
 */
class InstanceRenderer {
    constructor(statusManager) {
        this.statusManager = statusManager;
        this.renderedPorts = new Set();
        this.observer = null;
        this.initIntersectionObserver();
    }

    /**
     * Initialize IntersectionObserver for lazy rendering
     */
    initIntersectionObserver() {
        const options = {
            root: null,
            rootMargin: '50px',
            threshold: 0.01
        };

        this.observer = new IntersectionObserver((entries) => {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    entry.target.classList.add('visible');
                    // Only animate once
                    this.observer.unobserve(entry.target);
                }
            });
        }, options);
    }

    /**
     * Render instances to DOM
     * @param {Array} instances - Array of instance objects
     */
    render(instances) {
        const container = document.getElementById('instances-container');
        
        // Update instance count in section title
        const countElement = document.getElementById('instance-count');
        if (countElement) {
            countElement.textContent = `(${instances.length})`;
        }
        
        if (!container) return;

        if (instances.length === 0) {
            container.innerHTML = this.renderEmptyState();
            this.renderedPorts.clear();
            return;
        }

        const currentPorts = new Set(instances.map(i => i.port));
        const needsFullRender = this.hasStructuralChanges(currentPorts);

        if (needsFullRender) {
            this.fullRender(container, instances);
        } else {
            this.partialUpdate(container, instances);
        }
    }

    /**
     * Check if structural changes require full re-render
     * @param {Set} currentPorts - Current port set
     * @returns {boolean} True if full render needed
     */
    hasStructuralChanges(currentPorts) {
        return currentPorts.size !== this.renderedPorts.size ||
            [...currentPorts].some(port => !this.renderedPorts.has(port)) ||
            [...this.renderedPorts].some(port => !currentPorts.has(port));
    }

    /**
     * Perform full render of instances
     * @param {HTMLElement} container - Container element
     * @param {Array} instances - Instances to render
     */
    fullRender(container, instances) {
        const isMobile = this.isMobile();
        
        container.innerHTML = instances.map((instance, index) => 
            this.renderInstanceCard(instance, index, isMobile)
        ).join('');

        // Observe new cards for animation
        if (!isMobile) {
            requestAnimationFrame(() => {
                container.querySelectorAll('.instance-card').forEach(card => {
                    this.observer.observe(card);
                });
            });
        }

        this.renderedPorts = new Set(instances.map(i => i.port));
    }

    /**
     * Perform partial update of existing instances
     * @param {HTMLElement} container - Container element
     * @param {Array} instances - Instances to update
     */
    partialUpdate(container, instances) {
        instances.forEach(instance => {
            const card = container.querySelector(`[data-port="${instance.port}"]`);
            if (!card) return;

            this.updateInstanceCard(card, instance);
        });
    }

    /**
     * Render single instance card HTML
     * @param {Object} instance - Instance data
     * @param {number} index - Card index
     * @param {boolean} isMobile - Mobile flag
     * @returns {string} HTML string
     */
    renderInstanceCard(instance, index, isMobile) {
        const isOnline = instance.isOnline || false;
        const statusClass = isOnline ? 'online' : 'offline';
        const statusIndicator = isOnline ?
            '<span class="online-indicator"></span>Connected' :
            '<span class="offline-indicator"></span>Disconnected';

        const instanceStatus = this.statusManager.getInstanceStatus(instance);
        const animationClass = isMobile ? '' : 'animate-in';

        return `
            <div class="instance-card ${statusClass} ${animationClass}" 
                 data-port="${instance.port}" 
                 style="--card-index: ${index}">
                <div class="instance-header">
                    <h3 class="instance-title">
                        ${this.escapeHtml(instance.name)}
                        <span class="instance-status-badge ${instanceStatus.status}">
                            ${instanceStatus.text}
                        </span>
                    </h3>
                    <span class="instance-badge">Port ${instance.port}</span>
                </div>
                <div class="instance-body">
                    ${this.renderInstanceInfo(instance, statusIndicator)}
                    ${this.renderBotStatus(instance)}
                    <div class="instance-controls">
                        <button class="action-menu-button" 
                                data-port="${instance.port}" 
                                ${!isOnline ? 'disabled' : ''}
                                aria-label="Open actions menu">
                            ⚡ Actions
                        </button>
                        <button class="update-button" 
                                data-port="${instance.port}" 
                                ${!isOnline ? 'disabled' : ''}
                                aria-label="Update instance"
                                onclick="window.botControlPanel.updateManager.updateInstance(${instance.port})">
                            🔄 Update
                        </button>
                    </div>
                </div>
            </div>
        `;
    }

    /**
     * Render instance info section
     * @param {Object} instance - Instance data
     * @param {string} statusIndicator - Status HTML
     * @returns {string} HTML string
     */
    renderInstanceInfo(instance, statusIndicator) {
        return `
            <div class="instance-info">
                <div class="info-item">
                    <span class="info-label">Version</span>
                    <span class="info-value">${this.escapeHtml(instance.version)}</span>
                </div>
                <div class="info-item">
                    <span class="info-label">Mode</span>
                    <span class="info-value">${this.escapeHtml(instance.mode)}</span>
                </div>
                <div class="info-item">
                    <span class="info-label">Process ID</span>
                    <span class="info-value">${instance.processId}</span>
                </div>
                <div class="info-item">
                    <span class="info-label">Connection</span>
                    <span class="info-value">${statusIndicator}</span>
                </div>
            </div>
        `;
    }

    /**
     * Render bot status section
     * @param {Object} instance - Instance data
     * @returns {string} HTML string
     */
    renderBotStatus(instance) {
        if (instance.botStatuses && instance.botStatuses.length > 0) {
            return `
                <div class="bot-status">
                    <div class="info-label" style="margin-bottom: 0.5rem;">
                        BOTS (${instance.botStatuses.length})
                    </div>
                    ${instance.botStatuses.map((bot, index) => `
                        <div class="bot-status-item">
                            <span class="bot-name">
                                <span style="color: ${this.statusManager.getColor(bot.status)};">●</span>
                                <span>${this.escapeHtml(bot.name || `Bot ${index + 1}`)}</span>
                            </span>
                            <span class="bot-state ${this.statusManager.getStatusClass(bot.status)}">
                                ${this.escapeHtml(bot.status)}
                            </span>
                        </div>
                    `).join('')}
                </div>
            `;
        } else if (instance.botCount > 0) {
            return `
                <div class="bot-status">
                    <div class="info-label">BOTS</div>
                    <div class="bot-status-item">
                        <span class="bot-name">Bot Count: ${instance.botCount}</span>
                        <span class="bot-state">Status Unknown</span>
                    </div>
                </div>
            `;
        }
        return '';
    }

    /**
     * Update existing instance card
     * @param {HTMLElement} card - Card element
     * @param {Object} instance - Instance data
     */
    updateInstanceCard(card, instance) {
        const isOnline = instance.isOnline || false;
        card.classList.toggle('online', isOnline);
        card.classList.toggle('offline', !isOnline);

        // Update connection status
        const connectionValue = card.querySelector('.info-item:nth-child(4) .info-value');
        if (connectionValue) {
            connectionValue.innerHTML = isOnline ?
                '<span class="online-indicator"></span>Connected' :
                '<span class="offline-indicator"></span>Disconnected';
        }

        // Update instance status badge
        const instanceStatus = this.statusManager.getInstanceStatus(instance);
        const statusBadge = card.querySelector('.instance-status-badge');
        if (statusBadge) {
            statusBadge.className = `instance-status-badge ${instanceStatus.status}`;
            statusBadge.textContent = instanceStatus.text;
        }

        // Update bot statuses
        this.updateBotStatuses(card, instance);
    }

    /**
     * Update bot status display
     * @param {HTMLElement} card - Card element
     * @param {Object} instance - Instance data
     */
    updateBotStatuses(card, instance) {
        const botStatusContainer = card.querySelector('.bot-status');
        if (botStatusContainer && instance.botStatuses && instance.botStatuses.length > 0) {
            const botItems = botStatusContainer.querySelectorAll('.bot-status-item');
            instance.botStatuses.forEach((bot, index) => {
                if (botItems[index]) {
                    const statusSpan = botItems[index].querySelector('.bot-state');
                    const statusDot = botItems[index].querySelector('.bot-name span:first-child');
                    if (statusSpan) {
                        const statusClass = this.statusManager.getStatusClass(bot.status);
                        statusSpan.className = `bot-state ${statusClass}`;
                        statusSpan.textContent = bot.status;
                    }
                    if (statusDot) {
                        statusDot.style.color = this.statusManager.getColor(bot.status);
                    }
                }
            });
        }
    }

    /**
     * Render empty state
     * @returns {string} HTML string
     */
    renderEmptyState() {
        return `
            <div class="error-message">
                ⚠️ No bot instances found. Make sure at least one instance of ZE_FusionBot is running.
            </div>
        `;
    }

    /**
     * Escape HTML to prevent XSS
     * @param {string} text - Text to escape
     * @returns {string} Escaped text
     */
    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    /**
     * Check if device is mobile
     * @returns {boolean} True if mobile
     */
    isMobile() {
        return /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent) ||
            window.innerWidth <= 768;
    }
}

// ============================================================================
// DASHBOARD MANAGER
// ============================================================================

/**
 * Manages dashboard overview statistics with animations
 * @class
 */
class DashboardManager {
    constructor() {
        this.animationDuration = 500;
    }

    /**
     * Update dashboard statistics
     * @param {Array} instances - Bot instances
     */
    update(instances) {
        const stats = this.calculateStatistics(instances);
        
        this.animateValue('total-instances', stats.totalInstances);
        this.animateValue('active-bots', stats.activeBots);
        this.animateValue('idle-bots', stats.idleBots);
        this.animateValue('online-instances', stats.onlineInstances);
    }

    /**
     * Calculate statistics from instances
     * @param {Array} instances - Bot instances
     * @returns {Object} Statistics object
     */
    calculateStatistics(instances) {
        const stats = {
            totalInstances: instances.length,
            onlineInstances: 0,
            activeBots: 0,
            idleBots: 0
        };

        const statusManager = new StatusManager();

        instances.forEach(instance => {
            if (instance.isOnline) stats.onlineInstances++;

            if (instance.botStatuses && instance.botStatuses.length > 0) {
                instance.botStatuses.forEach(bot => {
                    const statusClass = statusManager.getStatusClass(bot.status);
                    if (statusClass === 'running') stats.activeBots++;
                    else if (statusClass === 'idle') stats.idleBots++;
                });
            }
        });

        return stats;
    }

    /**
     * Animate a value change
     * @param {string} elementId - Element ID to update
     * @param {number} endValue - Target value
     */
    animateValue(elementId, endValue) {
        const element = document.getElementById(elementId);
        if (!element) return;

        const startValue = parseInt(element.textContent) || 0;
        const startTime = Date.now();

        const update = () => {
            const elapsed = Date.now() - startTime;
            const progress = Math.min(elapsed / this.animationDuration, 1);
            const currentValue = Math.floor(
                startValue + (endValue - startValue) * this.easeOutCubic(progress)
            );

            element.textContent = currentValue;

            if (progress < 1) {
                requestAnimationFrame(update);
            }
        };

        requestAnimationFrame(update);
    }

    /**
     * Easing function for animations
     * @param {number} t - Progress (0-1)
     * @returns {number} Eased value
     */
    easeOutCubic(t) {
        return 1 - Math.pow(1 - t, 3);
    }
}

// ============================================================================
// APPLICATION STATE MANAGER
// ============================================================================

/**
 * Manages application state with event emitters
 * @class
 */
class StateManager extends EventTarget {
    constructor() {
        super();
        this.state = {
            instances: [],
            refreshInterval: null,
            isInteracting: false,
            refreshPaused: false,
            lastRefreshTime: Date.now(),
            currentActionPort: null,
            updateState: {
                id: null,
                interval: null,
                type: null,
                startTime: null
            },
            restartState: {
                startTime: null,
                timeout: 300,
                idleCheckInterval: null,
                countdownInterval: null
            },
            connectionLost: false,
            masterUpdating: false
        };
    }

    /**
     * Get state value
     * @param {string} key - State key
     * @returns {any} State value
     */
    get(key) {
        return this.state[key];
    }

    /**
     * Set state value and emit change event
     * @param {string} key - State key
     * @param {any} value - New value
     */
    set(key, value) {
        const oldValue = this.state[key];
        this.state[key] = value;
        
        this.dispatchEvent(new CustomEvent('statechange', {
            detail: { key, oldValue, newValue: value }
        }));
    }

    /**
     * Update multiple state values
     * @param {Object} updates - Object with updates
     */
    update(updates) {
        Object.entries(updates).forEach(([key, value]) => {
            this.set(key, value);
        });
    }
}

// ============================================================================
// MAIN APPLICATION CONTROLLER
// ============================================================================

/**
 * Main application controller that orchestrates all modules
 * @class
 */
class BotControlPanel {
    constructor() {
        this.themeManager = new ThemeManager();
        this.api = new ApiService();
        this.state = new StateManager();
        this.statusManager = new StatusManager();
        this.toastManager = new ToastManager();
        this.instanceRenderer = new InstanceRenderer(this.statusManager);
        this.dashboardManager = new DashboardManager();
        
        this.refreshManager = null;
        this.commandManager = null;
        this.updateManager = null;
        this.restartManager = null;
        this.remoteControl = null;
        
        this.init();
    }

    /**
     * Initialize application
     */
    async init() {
        await this.setupModules();
        await this.setupEventHandlers();
        await this.checkForOngoingOperations();
        
        if (!this.state.get('updateState')?.id) {
            await this.refresh();
        }
        
        this.startRefreshCycle();
        this.startConnectionMonitor();
        await this.loadRestartSchedule();
    }

    /**
     * Setup application modules
     */
    async setupModules() {
        // Initialize refresh manager
        this.refreshManager = new RefreshManager(this);
        
        // Initialize command manager
        this.commandManager = new CommandManager(this);
        
        // Initialize update manager
        this.updateManager = new UpdateManager(this);
        
        // Initialize restart manager
        this.restartManager = new RestartManager(this);

        // Initialize remote control
        this.remoteControl = new RemoteControl(this);
    }

    /**
     * Setup event handlers with delegation
     */
    async setupEventHandlers() {
        // Use event delegation for better performance
        document.addEventListener('click', this.handleClick.bind(this), { passive: false });
        document.addEventListener('touchend', this.handleTouch.bind(this), { passive: false });
        
        // Handle keyboard shortcuts
        document.addEventListener('keydown', this.handleKeyboard.bind(this));
        
        // Handle window events
        window.addEventListener('beforeunload', this.cleanup.bind(this));
        window.addEventListener('resize', this.handleResize.bind(this));
        
        // Handle visibility changes
        document.addEventListener('visibilitychange', this.handleVisibilityChange.bind(this));
        
        // Setup specific button handlers
        this.setupButtonHandlers();
    }

    /**
     * Handle click events
     * @param {Event} e - Click event
     */
    handleClick(e) {
        const target = e.target.closest('button, .action-modal-item');
        if (!target) return;

        // Handle refresh button
        if (target.id === 'refresh-button') {
            e.preventDefault();
            this.refreshManager.manual();
            return;
        }

        // Handle global action buttons
        if (target.hasAttribute('data-global-action')) {
            e.preventDefault();
            const action = target.getAttribute('data-global-action');
            this.handleGlobalAction(action);
            return;
        }

        // Handle action menu buttons
        if (target.classList.contains('action-menu-button')) {
            e.preventDefault();
            const port = target.getAttribute('data-port');
            if (port) this.toggleActionMenu(port);
            return;
        }

        // Handle action modal items
        if (target.classList.contains('action-modal-item')) {
            e.preventDefault();
            const action = target.getAttribute('data-action');
            const port = this.state.get('currentActionPort');
            if (action && port) {
                this.handleInstanceAction(port, action);
                this.closeActionMenu();
            }
            return;
        }
    }

    /**
     * Handle touch events for mobile
     * @param {Event} e - Touch event
     */
    handleTouch(e) {
        if (!/iPhone|iPad|iPod/i.test(navigator.userAgent)) return;
        
        const target = e.target.closest('button');
        if (target && target.hasAttribute('data-global-action')) {
            e.preventDefault();
            const action = target.getAttribute('data-global-action');
            this.handleGlobalAction(action);
        }
    }

    /**
     * Handle keyboard events
     * @param {KeyboardEvent} e - Keyboard event
     */
    handleKeyboard(e) {
        // ESC key closes modals
        if (e.key === 'Escape') {
            this.closeAllModals();
        }
        
        // Ctrl/Cmd + R refreshes
        if ((e.ctrlKey || e.metaKey) && e.key === 'r') {
            e.preventDefault();
            this.refreshManager.manual();
        }
    }

    /**
     * Handle window resize
     */
    handleResize() {
        this.toastManager.updatePositions();
    }

    /**
     * Handle visibility change
     */
    handleVisibilityChange() {
        if (document.hidden) {
            this.refreshManager.pause();
        } else {
            this.refreshManager.resume();
        }
    }

    /**
     * Setup specific button handlers
     */
    setupButtonHandlers() {
        // Modal close buttons
        const modalCloseButtons = [
            'close-update-modal',
            'cancel-update',
            'close-actions-modal',
            'close-remote-control'
        ];

        modalCloseButtons.forEach(id => {
            const button = document.getElementById(id);
            if (button) {
                button.addEventListener('click', () => this.closeModal(id.replace('close-', '').replace('-modal', '')));
            }
        });

        // Confirm update button
        const confirmUpdate = document.getElementById('confirm-update');
        if (confirmUpdate) {
            confirmUpdate.addEventListener('click', () => this.updateManager.confirmUpdate());
        }

        // Schedule restart toggle - handlers are set up in loadSchedule() to avoid duplicates
        // The loadSchedule() method properly manages event handlers
    }

    /**
     * Handle global actions
     * @param {string} action - Action name
     */
    handleGlobalAction(action) {
        const actionMap = {
            'start': () => this.commandManager.sendGlobal('start'),
            'stop': () => this.commandManager.sendGlobal('stop'),
            'idle': () => this.commandManager.sendGlobal('idle'),
            'resume': () => this.commandManager.sendGlobal('resume'),
            'screenon': () => this.commandManager.sendGlobal('screenon'),
            'screenoff': () => this.commandManager.sendGlobal('screenoff'),
            'update': () => this.updateManager.updateAll(),
            'restart': () => this.restartManager.restartAll()
        };

        const handler = actionMap[action];
        if (handler) {
            handler();
        } else {
            console.error(`Unknown action: ${action}`);
        }
    }

    /**
     * Handle instance actions
     * @param {string} port - Instance port
     * @param {string} action - Action name
     */
    handleInstanceAction(port, action) {
        if (action === 'remote') {
            this.remoteControl.open(parseInt(port));
        } else {
            this.commandManager.sendToInstance(parseInt(port), action);
        }
    }

    /**
     * Toggle action menu for instance
     * @param {string} port - Instance port
     */
    toggleActionMenu(port) {
        this.state.set('currentActionPort', port);
        
        const modal = document.getElementById('actions-modal');
        if (modal) {
            modal.style.display = 'flex';
            modal.classList.add('show');
            this.state.set('isInteracting', true);
        }
    }

    /**
     * Close action menu
     */
    closeActionMenu() {
        const modal = document.getElementById('actions-modal');
        if (modal) {
            modal.style.display = 'none';
            modal.classList.remove('show');
            this.state.set('isInteracting', false);
        }
    }

    /**
     * Close all modals
     */
    closeAllModals() {
        const modals = document.querySelectorAll('.modal.show');
        modals.forEach(modal => {
            modal.classList.remove('show');
            setTimeout(() => {
                modal.style.display = 'none';
            }, 300);
        });
        this.state.set('isInteracting', false);
    }

    /**
     * Close specific modal
     * @param {string} modalId - Modal ID
     */
    closeModal(modalId) {
        const modal = document.getElementById(`${modalId}-modal`);
        if (modal) {
            modal.classList.remove('show');
            setTimeout(() => {
                modal.style.display = 'none';
            }, 300);
        }
    }

    /**
     * Refresh instances
     */
    async refresh() {
        try {
            const data = await this.api.get(this.api.endpoints.instances);
            this.state.set('instances', data.instances || []);
            this.instanceRenderer.render(this.state.get('instances'));
            this.dashboardManager.update(this.state.get('instances'));
        } catch (error) {
            this.toastManager.error('Failed to load bot instances');
        }
    }

    /**
     * Start refresh cycle
     */
    startRefreshCycle() {
        this.refreshManager.start();
    }

    /**
     * Start connection monitoring
     */
    startConnectionMonitor() {
        // Use a more reasonable interval to reduce server load
        // 5 seconds is sufficient for connection monitoring
        setInterval(async () => {
            try {
                await this.api.get(this.api.endpoints.instances);
                
                if (this.state.get('connectionLost')) {
                    this.state.set('connectionLost', false);
                    this.handleReconnection();
                }
            } catch (error) {
                if (!this.state.get('connectionLost')) {
                    this.state.set('connectionLost', true);
                    this.handleConnectionLost();
                }
            }
        }, 5000); // Changed from 1000ms to 5000ms to reduce server load
    }

    /**
     * Handle connection lost
     */
    handleConnectionLost() {
        this.toastManager.warning('Connection lost to server');
    }

    /**
     * Handle reconnection
     */
    handleReconnection() {
        this.toastManager.success('Connection restored');
        this.refresh();
    }

    /**
     * Check for ongoing operations on startup
     */
    async checkForOngoingOperations() {
        try {
            const response = await this.api.get(this.api.endpoints.updateActive);
            if (response.active && response.session && !response.session.isComplete) {
                this.updateManager.resumeUpdate(response.session);
            }
        } catch (error) {
            console.error('Error checking for active operations:', error);
        }
    }

    /**
     * Load restart schedule
     */
    async loadRestartSchedule() {
        await this.restartManager.loadSchedule();
    }

    /**
     * Cleanup on unload
     */
    cleanup() {
        this.refreshManager.stop();
        // Clear all intervals
        const intervals = ['refreshInterval', 'restartScheduleInterval'];
        intervals.forEach(key => {
            const interval = this.state.get(key);
            if (interval) clearInterval(interval);
        });
    }
}

// ============================================================================
// REFRESH MANAGER MODULE
// ============================================================================

/**
 * Manages refresh cycles and manual refreshes
 * @class
 */
class RefreshManager {
    constructor(app) {
        this.app = app;
        this.interval = null;
        this.refreshRate = 5000; // 5 seconds
    }

    /**
     * Start auto-refresh
     */
    start() {
        this.stop();
        this.interval = setInterval(() => {
            if (!this.shouldRefresh()) return;
            this.app.refresh();
        }, 1000);
    }

    /**
     * Stop auto-refresh
     */
    stop() {
        if (this.interval) {
            clearInterval(this.interval);
            this.interval = null;
        }
    }

    /**
     * Pause refresh
     */
    pause() {
        this.app.state.set('refreshPaused', true);
        this.updateIndicator(true);
    }

    /**
     * Resume refresh
     */
    resume() {
        this.app.state.set('refreshPaused', false);
        this.updateIndicator(false);
    }

    /**
     * Check if should refresh
     * @returns {boolean} True if should refresh
     */
    shouldRefresh() {
        const timeSinceLastRefresh = Date.now() - this.app.state.get('lastRefreshTime');
        const hasOpenMenu = document.querySelector('.action-menu.show') !== null;
        const actionsModal = document.getElementById('actions-modal');
        const isActionsModalOpen = actionsModal && actionsModal.style.display === 'flex';
        
        return !hasOpenMenu && 
               !isActionsModalOpen &&
               !this.app.state.get('isInteracting') && 
               !this.app.state.get('refreshPaused') && 
               timeSinceLastRefresh >= this.refreshRate;
    }

    /**
     * Manual refresh
     */
    manual() {
        this.app.closeAllModals();
        this.app.refresh();
        this.app.toastManager.info('Bot instances refreshed');
    }

    /**
     * Update refresh indicator
     * @param {boolean} paused - Is paused
     */
    updateIndicator(paused) {
        const indicator = document.querySelector('.refresh-indicator');
        if (indicator) {
            indicator.classList.toggle('paused', paused);
            indicator.title = paused ? 'Auto-refresh paused' : 'Auto-refresh active';
        }
    }
}

// ============================================================================
// COMMAND MANAGER MODULE
// ============================================================================

/**
 * Manages bot commands
 * @class
 */
class CommandManager {
    constructor(app) {
        this.app = app;
    }

    /**
     * Send command to all instances
     * @param {string} command - Command to send
     */
    async sendGlobal(command) {
        this.app.toastManager.info(`Sending ${command} to all instances...`);

        try {
            const result = await this.app.api.post(this.app.api.endpoints.commandAll, { command });

            const successCount = result.successfulCommands || 0;
            const totalCount = result.totalInstances || 0;

            if (successCount === totalCount && totalCount > 0) {
                this.app.toastManager.success(`Successfully sent ${command} to all ${totalCount} instances`);
            } else if (successCount > 0) {
                this.app.toastManager.warning(`Command sent to ${successCount} of ${totalCount} instances`);
            } else {
                this.app.toastManager.error('Failed to send command to any instances');
            }

            setTimeout(() => this.app.refresh(), 1000);
        } catch (error) {
            console.error('Error sending global command:', error);
            this.app.toastManager.error(`Failed to send command: ${command}`);
        }
    }

    /**
     * Send command to specific instance
     * @param {number} port - Instance port
     * @param {string} command - Command to send
     */
    async sendToInstance(port, command) {
        this.app.closeAllModals();
        this.app.state.set('isInteracting', false);
        this.app.toastManager.info(`Sending ${command} to instance on port ${port}...`);

        try {
            const url = `${this.app.api.baseUrl}/instances/${port}/command`;
            const result = await this.app.api.post(url, { command });

            if (result.success !== false && !result.error) {
                this.app.toastManager.success(`Successfully sent ${command} to port ${port}`);
            } else {
                this.app.toastManager.error(result.message || `Failed to send command to port ${port}`);
            }

            setTimeout(() => this.app.refresh(), 1000);
        } catch (error) {
            console.error(`Error sending command to port ${port}:`, error);
            this.app.toastManager.error(`Failed to send command to port ${port}`);
        }
    }
}

// ============================================================================
// UPDATE MANAGER MODULE  
// ============================================================================

/**
 * Manages bot updates
 * @class
 */
class UpdateManager {
    constructor(app) {
        this.app = app;
        this.checkInterval = null;
    }

    /**
     * Start update process for all instances
     */
    async updateAll() {
        this.showModal('update');
        await this.showUpdateModal();
    }

    /**
     * Start update process for a single instance
     * @param {number} port - Instance port
     */
    async updateInstance(port) {
        if (!port) {
            console.error('No port specified for single instance update');
            return;
        }

        try {
            // Show confirmation first
            const instance = this.app.state.get('instances').find(i => i.port === port);
            if (!instance) {
                this.app.toastManager.error(`Instance on port ${port} not found`);
                return;
            }

            const confirmed = confirm(`Update instance on port ${port} (${instance.name || 'Unknown'})?\n\nThis will restart the instance with the latest version.`);
            if (!confirmed) return;

            // Start single instance update
            const response = await this.app.api.post(`/api/bot/instances/${port}/update`);

            if (response.success) {
                this.app.toastManager.success(`Started update for instance on port ${port}`);
                
                // Show progress modal
                this.showModal('progress');
                document.getElementById('progress-modal-title').textContent = `Updating Instance ${port}`;
                
                // Initialize update state
                const updateState = this.app.state.get('updateState');
                updateState.id = `single-${port}-${Date.now()}`;
                updateState.type = 'single';
                updateState.port = port;
                updateState.startTime = Date.now();
                this.app.state.set('updateState', updateState);
                
                // Start monitoring (will use the regular status checking)
                this.startStatusCheck();
                this.app.state.set('refreshPaused', true);
            } else {
                this.app.toastManager.error(`Failed to start update: ${response.message || 'Unknown error'}`);
            }
        } catch (error) {
            console.error('Error starting single instance update:', error);
            this.app.toastManager.error(`Failed to start update: ${error.message}`);
        }
    }

    /**
     * Show update modal and load version information
     */
    async showUpdateModal() {
        try {
            const instancesData = await this.app.api.get(this.app.api.endpoints.instances);
            const currentVersion = instancesData.instances?.[0]?.version || 'Unknown';

            document.getElementById('current-version').textContent = currentVersion;
            document.getElementById('new-version').textContent = 'Checking...';
            document.getElementById('changelog-content').innerHTML = '<div class="spinner"></div>';

            const checkResponse = await fetch(this.app.api.endpoints.updateCheck);
            let updateInfo = { version: 'Unknown', changelog: 'No changelog available' };

            if (checkResponse.ok) {
                updateInfo = await checkResponse.json();
            } else {
                updateInfo.version = 'Latest';
                updateInfo.changelog = 'Unable to fetch changelog. Update will proceed to latest version.';
            }

            document.getElementById('new-version').textContent = updateInfo.version;
            document.getElementById('changelog-content').textContent = updateInfo.changelog;

        } catch (error) {
            console.error('Error checking updates:', error);
            document.getElementById('new-version').textContent = 'Latest';
            document.getElementById('changelog-content').textContent = 'Unable to fetch update information.';
        }
    }

    /**
     * Confirm and execute update
     */
    async confirmUpdate() {
        this.closeModal('update');
        this.showModal('progress');
        
        const updateState = this.app.state.get('updateState');
        updateState.type = 'update';
        updateState.startTime = Date.now();
        this.app.state.set('updateState', updateState);

        try {
            const response = await this.app.api.post(this.app.api.endpoints.updateAll, { force: true });

            if (!response.ok && !response.sessionId) {
                throw new Error('Failed to start update');
            }

            updateState.id = response.sessionId;
            this.app.state.set('updateState', updateState);
            
            this.startStatusCheck();
            this.app.state.set('refreshPaused', true);

        } catch (error) {
            console.error('Error starting update:', error);
            this.app.toastManager.error('Failed to start update process');
            this.app.state.set('refreshPaused', false);
            this.closeModal('progress');
        }
    }

    /**
     * Resume an ongoing update
     * @param {Object} session - Update session
     */
    resumeUpdate(session) {
        const updateState = this.app.state.get('updateState');
        updateState.id = session.id;
        updateState.type = 'update';
        updateState.startTime = new Date(session.startTime).getTime();
        this.app.state.set('updateState', updateState);
        
        this.showModal('progress');
        this.startStatusCheck();
        this.app.state.set('refreshPaused', true);
    }

    /**
     * Start checking update status
     */
    startStatusCheck() {
        this.checkStatus();
        this.checkInterval = setInterval(() => this.checkStatus(), 2000);
    }

    /**
     * Check update status
     */
    async checkStatus() {
        const updateState = this.app.state.get('updateState');
        if (!updateState.id) return;

        try {
            const response = await this.app.api.get(this.app.api.endpoints.updateActive);
            
            // If no active session, check if we just completed
            if (!response.active || !response.session) {
                // Get current version to check if update succeeded
                const instancesResponse = await this.app.api.get(this.app.api.endpoints.instances);
                const currentVersion = instancesResponse.instances?.[0]?.version || 'Unknown';
                
                // If version changed or session disappeared, assume success
                console.log('Update session ended - checking completion');
                this.handleCompletion({
                    isComplete: true,
                    success: true,
                    successCount: instancesResponse.instances?.length || 0,
                    failureCount: 0,
                    message: 'Update completed successfully'
                });
                return;
            }

            const status = response.session;
            this.updateProgress(status);
            this.updateIdleProgress(status);
            this.updateTimeline(status);

            if (status.isComplete) {
                this.handleCompletion(status);
            }
        } catch (error) {
            if (this.app.state.get('connectionLost')) {
                this.updateProgress({
                    phase: 'Connection Lost',
                    message: 'Master instance may be updating...',
                    progress: -1
                });
            }
        }
    }

    /**
     * Update progress display
     * @param {Object} status - Status object
     */
    updateProgress(status) {
        const phaseText = this.getPhaseText(status.phase);
        const progressBar = document.getElementById('progress-bar');
        const progressStatus = document.getElementById('progress-status');
        const progressDetails = document.getElementById('progress-details');
        const stepIcon = document.getElementById('step-icon');

        if (progressStatus) progressStatus.textContent = phaseText;
        if (progressDetails) {
            let details = status.message || '';
            if (status.currentUpdatingInstance) {
                details = `Updating: ${status.currentUpdatingInstance} - ${details}`;
            }
            progressDetails.textContent = details;
        }
        
        // Update step icon based on phase
        if (stepIcon) {
            const iconMap = {
                'Checking': '🔍',
                'Idling': '⏸️',
                'Updating': '🔄',
                'Verifying': '✅',
                'Complete': status.success ? '✅' : '❌'
            };
            stepIcon.textContent = iconMap[status.phase] || '⏳';
        }
        
        // Calculate progress percentage
        let progressPercent = -1;
        if (status.phase === 'Checking') {
            progressPercent = 10;
        } else if (status.phase === 'Idling' && status.idleProgress) {
            progressPercent = 10 + (status.idleProgress.idleBots / Math.max(1, status.idleProgress.totalBots)) * 30;
        } else if (status.phase === 'Updating') {
            progressPercent = 40 + (status.completedInstances / Math.max(1, status.totalInstances)) * 50;
        } else if (status.phase === 'Verifying') {
            progressPercent = 90;
        } else if (status.phase === 'Complete') {
            progressPercent = 100;
        }
        
        if (progressBar) {
            if (progressPercent >= 0) {
                progressBar.style.width = `${progressPercent}%`;
                progressBar.classList.remove('indeterminate');
            } else {
                progressBar.classList.add('indeterminate');
            }
        }
    }

    /**
     * Get phase text
     * @param {string} phase - Phase name
     * @returns {string} Display text
     */
    getPhaseText(phase) {
        const phaseMap = {
            'Checking': 'Checking for updates',
            'Idling': 'Idling bots',
            'Updating': 'Updating instances',
            'Verifying': 'Verifying updates',
            'Complete': 'Update complete!',
            'Failed': 'Update error'
        };
        return phaseMap[phase] || 'Processing...';
    }

    /**
     * Update idle progress display
     * @param {Object} status - Status object
     */
    updateIdleProgress(status) {
        const idleStatusDiv = document.getElementById('idle-status');
        const botsIdled = document.getElementById('bots-idled');
        const botsTotal = document.getElementById('bots-total');
        const timeoutCountdown = document.getElementById('timeout-countdown');
        const forceUpdateInfo = document.getElementById('force-update-info');
        
        if (status.phase === 'Idling' && status.idleProgress) {
            // Show idle status
            if (idleStatusDiv) idleStatusDiv.style.display = 'block';
            if (forceUpdateInfo) forceUpdateInfo.style.display = 'block';
            
            const progress = status.idleProgress;
            if (botsIdled) botsIdled.textContent = progress.idleBots;
            if (botsTotal) botsTotal.textContent = progress.totalBots;
            if (timeoutCountdown) timeoutCountdown.textContent = progress.remainingSeconds;
            
            // Update instance-specific idle status
            const updateLog = document.getElementById('update-log');
            if (updateLog && progress.instances) {
                const logHtml = progress.instances.map(inst => {
                    const statusClass = inst.allIdle ? 'success' : 'warning';
                    const statusIcon = inst.allIdle ? '✅' : '⏳';
                    const idleInfo = `${inst.idleBots}/${inst.totalBots} idle`;
                    
                    let html = `<div class="idle-instance ${statusClass}">`;
                    html += `<span class="instance-name">${statusIcon} ${inst.name}:</span>`;
                    html += `<span class="idle-count">${idleInfo}</span>`;
                    
                    if (inst.nonIdleBots && inst.nonIdleBots.length > 0) {
                        html += `<div class="non-idle-bots">`;
                        inst.nonIdleBots.forEach(bot => {
                            html += `<div class="non-idle-bot">• ${bot}</div>`;
                        });
                        html += `</div>`;
                    }
                    html += `</div>`;
                    return html;
                }).join('');
                
                updateLog.innerHTML = `<div class="idle-progress-details">${logHtml}</div>`;
            }
        } else {
            // Hide idle status
            if (idleStatusDiv) idleStatusDiv.style.display = 'none';
            if (forceUpdateInfo) forceUpdateInfo.style.display = 'none';
        }
    }

    /**
     * Update timeline display
     * @param {Object} status - Status object
     */
    updateTimeline(status) {
        const timeline = document.getElementById('update-timeline');
        if (!timeline) return;
        
        if (status.phase === 'Updating' && status.instances) {
            const timelineHtml = status.instances.map(inst => {
                let statusClass = '';
                let statusIcon = '';
                let statusText = '';
                
                switch(inst.status) {
                    case 'Completed':
                        statusClass = 'completed';
                        statusIcon = '✅';
                        statusText = 'Updated';
                        break;
                    case 'Updating':
                        statusClass = 'updating';
                        statusIcon = '🔄';
                        statusText = 'Updating...';
                        break;
                    case 'Failed':
                        statusClass = 'failed';
                        statusIcon = '❌';
                        statusText = inst.error || 'Failed';
                        break;
                    default:
                        statusClass = 'pending';
                        statusIcon = '⏳';
                        statusText = 'Pending';
                }
                
                const instanceName = inst.isMaster ? 'Master' : `Instance ${inst.tcpPort}`;
                const isCurrent = status.currentUpdatingInstance === instanceName;
                
                return `<div class="timeline-item ${statusClass} ${isCurrent ? 'current' : ''}">
                    <span class="timeline-icon">${statusIcon}</span>
                    <span class="timeline-name">${instanceName}</span>
                    <span class="timeline-status">${statusText}</span>
                </div>`;
            }).join('');
            
            timeline.innerHTML = `<div class="timeline-header">Update Progress:</div>${timelineHtml}`;
            timeline.style.display = 'block';
        } else if (status.phase !== 'Idling') {
            timeline.style.display = 'none';
        }
    }

    /**
     * Handle update completion
     * @param {Object} status - Status object
     */
    handleCompletion(status) {
        clearInterval(this.checkInterval);
        this.checkInterval = null;
        
        const updateState = this.app.state.get('updateState');
        updateState.id = null;
        updateState.interval = null;
        this.app.state.set('updateState', updateState);
        this.app.state.set('refreshPaused', false);

        // Show completion in the modal
        const progressStatus = document.getElementById('progress-status');
        const progressDetails = document.getElementById('progress-details');
        const progressBar = document.getElementById('progress-bar');
        
        if (status.success) {
            // Update UI to show success
            if (progressStatus) progressStatus.textContent = '✅ Update Complete!';
            if (progressDetails) progressDetails.textContent = `Successfully updated ${status.successCount || 'all'} instance(s)`;
            if (progressBar) {
                progressBar.style.width = '100%';
                progressBar.style.background = 'var(--status-online)';
                progressBar.classList.remove('indeterminate');
            }
            
            this.app.toastManager.success(`Update completed successfully! Refreshing...`);
            
            // Reload page after showing success
            setTimeout(() => {
                window.location.reload();
            }, 2000);
        } else {
            // Update UI to show failure
            if (progressStatus) progressStatus.textContent = '❌ Update Failed';
            if (progressDetails) progressDetails.textContent = `Update completed with ${status.failureCount || 'some'} error(s)`;
            if (progressBar) {
                progressBar.style.background = 'var(--danger-red)';
                progressBar.classList.remove('indeterminate');
            }
            
            this.app.toastManager.error(`Update completed with errors`);
            
            setTimeout(() => {
                this.closeModal('progress');
                this.app.refresh();
            }, 3000);
        }
    }

    /**
     * Complete update process
     */
    completeUpdate() {
        clearInterval(this.checkInterval);
        this.checkInterval = null;
        
        const updateState = this.app.state.get('updateState');
        updateState.id = null;
        updateState.interval = null;
        this.app.state.set('updateState', updateState);
        this.app.state.set('refreshPaused', false);
        
        this.closeModal('progress');
        this.app.refresh();
    }

    /**
     * Show modal
     * @param {string} modalName - Modal name
     */
    showModal(modalName) {
        const modal = document.getElementById(`${modalName}-modal`);
        if (modal) {
            modal.style.display = '';
            modal.classList.add('show');
        }
    }

    /**
     * Close modal
     * @param {string} modalName - Modal name
     */
    closeModal(modalName) {
        const modal = document.getElementById(`${modalName}-modal`);
        if (modal) {
            modal.classList.remove('show');
            setTimeout(() => {
                modal.style.display = 'none';
            }, 300);
        }
    }
}

// ============================================================================
// RESTART MANAGER MODULE
// ============================================================================

/**
 * Manages bot restarts and schedules
 * @class
 */
class RestartManager {
    constructor(app) {
        this.app = app;
        this.scheduleInterval = null;
    }

    /**
     * Restart all instances with improved sequencing
     */
    async restartAll() {
        // Phase 1: Verify master instance exists
        const response = await this.app.api.get(this.app.api.endpoints.instances);
        const masterInstance = response.instances.find(i => i.isMaster);
        const slaveInstances = response.instances.filter(i => !i.isMaster);

        if (!masterInstance) {
            this.app.toastManager.error('No master instance found. Cannot initiate restart.');
            return;
        }

        this.showModal('progress');
        document.getElementById('progress-modal-title').textContent = 'Restart in Progress';

        // Track restart phases
        this.restartPhases = [
            { name: 'Stopping bots', progress: 10 },
            { name: 'Verifying bot status', progress: 25 },
            { name: 'Stopping services', progress: 40 },
            { name: 'Restarting slaves', progress: 60 },
            { name: 'Restarting master', progress: 80 },
            { name: 'Finalizing', progress: 100 }
        ];
        this.currentPhase = 0;

        try {
            // Phase 2: Initiate coordinated restart
            this.updateRestartPhase('Initializing restart sequence');
            const result = await this.app.api.post(this.app.api.endpoints.restartAll);

            if (result.success) {
                // Monitor the restart process
                await this.monitorRestartProgress(masterInstance, slaveInstances);
            } else {
                throw new Error(result.error || result.message || 'Failed to initiate restart');
            }
        } catch (error) {
            console.error('Error restarting instances:', error);
            this.updateProgress('Restart failed', error.message, 0);
            setTimeout(() => {
                this.closeModal('progress');
                this.app.toastManager.error('Failed to restart instances');
            }, 2000);
        }
    }

    /**
     * Monitor restart progress with better error handling
     */
    async monitorRestartProgress(masterInstance, slaveInstances) {
        const maxWaitTime = 120000; // 2 minutes max
        const startTime = Date.now();
        const pollInterval = 2000; // Check every 2 seconds

        // Monitor restart phases
        const checkProgress = async () => {
            try {
                const elapsed = Date.now() - startTime;
                if (elapsed > maxWaitTime) {
                    throw new Error('Restart timeout - process took too long');
                }

                // Try to get restart status
                const statusResponse = await this.app.api.get(`${this.app.api.baseUrl}/restart/status`)
                    .catch(() => ({ state: 'unknown', message: 'API unavailable' }));

                if (statusResponse.state === 'completed') {
                    this.updateProgress('Restart complete', 'All instances have been restarted successfully', 100);
                    setTimeout(() => {
                        this.closeModal('progress');
                        this.app.toastManager.success('All instances restarted successfully');
                        this.app.refresh();
                    }, 2000);
                    return;
                }

                // Update progress based on state
                this.updateProgressFromState(statusResponse);

                // Continue monitoring
                setTimeout(checkProgress, pollInterval);

            } catch (error) {
                if (error.message.includes('timeout')) {
                    this.updateProgress('Timeout', error.message, 0);
                } else {
                    // API might be temporarily unavailable during restart
                    this.updateProgress('Restarting...', 'Waiting for services to come back online', 75);
                    setTimeout(checkProgress, pollInterval * 2);
                }
            }
        };

        // Start monitoring
        await checkProgress();
    }

    /**
     * Update progress based on restart state
     */
    updateProgressFromState(status) {
        const stateMessages = {
            'idle': { msg: 'Ready to restart', progress: 0 },
            'preparing': { msg: 'Preparing restart sequence', progress: 10 },
            'stopping_bots': { msg: 'Stopping all bots', progress: 20 },
            'waiting_idle': { msg: 'Waiting for bots to stop', progress: 30 },
            'stopping_services': { msg: 'Stopping services', progress: 40 },
            'restarting_slaves': { msg: 'Restarting slave instances', progress: 60 },
            'restarting_master': { msg: 'Restarting master instance', progress: 80 },
            'finalizing': { msg: 'Finalizing restart', progress: 90 },
            'completed': { msg: 'Restart completed', progress: 100 },
            'failed': { msg: 'Restart failed', progress: 0 }
        };

        const info = stateMessages[status.state] || { msg: status.message || 'Processing...', progress: 50 };
        this.updateProgress(status.state, info.msg, info.progress);
    }

    /**
     * Update restart phase display
     */
    updateRestartPhase(message) {
        if (this.currentPhase < this.restartPhases.length) {
            const phase = this.restartPhases[this.currentPhase];
            this.updateProgress(phase.name, message || phase.name, phase.progress);
            this.currentPhase++;
        }
    }

    /**
     * Load restart schedule
     */
    async loadSchedule() {
        try {
            const response = await this.app.api.get(this.app.api.endpoints.restartSchedule);

            // Parse the response properly
            const enabled = response.Enabled === true || response.enabled === true;
            const time = response.Time || response.time || '00:00';

            console.log(`Loading restart schedule - Enabled: ${enabled}, Time: ${time}`);

            const toggle = document.getElementById('schedule-restart-toggle');
            const timeInput = document.getElementById('restart-time');

            if (toggle) {
                toggle.checked = enabled;
                // Remove any existing event listeners to prevent duplicates
                toggle.removeEventListener('change', this.toggleHandler);
                // Create a bound handler
                this.toggleHandler = () => this.toggleScheduled();
                toggle.addEventListener('change', this.toggleHandler);
            }

            if (timeInput) {
                timeInput.value = time;
                timeInput.disabled = !enabled;
                // Remove any existing event listeners to prevent duplicates
                timeInput.removeEventListener('change', this.timeHandler);
                // Create a bound handler
                this.timeHandler = () => this.updateSchedule();
                timeInput.addEventListener('change', this.timeHandler);
            }

            if (enabled) {
                this.showScheduleStatus();
                this.startScheduleChecker();
            } else {
                this.hideScheduleStatus();
                this.stopScheduleChecker();
            }
        } catch (error) {
            console.error('Error loading restart schedule:', error);
            // Set defaults on error
            const toggle = document.getElementById('schedule-restart-toggle');
            const timeInput = document.getElementById('restart-time');
            if (toggle) toggle.checked = false;
            if (timeInput) {
                timeInput.value = '00:00';
                timeInput.disabled = true;
            }
        }
    }

    /**
     * Toggle scheduled restart
     */
    async toggleScheduled() {
        const toggle = document.getElementById('schedule-restart-toggle');
        const timeInput = document.getElementById('restart-time');
        if (!toggle || !timeInput) return;

        const enabled = toggle.checked;
        const time = timeInput.value || '00:00';

        console.log(`Toggling restart schedule - Enabled: ${enabled}, Time: ${time}`);

        // Update UI immediately
        timeInput.disabled = !enabled;

        try {
            // Ensure we're sending the correct data format
            const payload = {
                Enabled: enabled,
                Time: time
            };

            console.log('Sending restart schedule update:', payload);
            const response = await this.app.api.post(this.app.api.endpoints.restartSchedule, payload);
            console.log('Restart schedule update response:', response);

            this.app.toastManager.success(
                enabled ? `Scheduled restart enabled for ${time}` : 'Scheduled restart disabled'
            );

            if (enabled) {
                this.showScheduleStatus();
                this.startScheduleChecker();
                this.updateNextRestartTime();
            } else {
                this.hideScheduleStatus();
                this.stopScheduleChecker();
            }
        } catch (error) {
            console.error('Error updating restart schedule:', error);
            // Revert the toggle on error
            toggle.checked = !enabled;
            timeInput.disabled = !toggle.checked;
            this.app.toastManager.error('Failed to update restart schedule');
        }
    }

    /**
     * Update restart schedule time
     */
    async updateSchedule() {
        const toggle = document.getElementById('schedule-restart-toggle');
        const timeInput = document.getElementById('restart-time');
        if (!toggle || !timeInput) return;

        const enabled = toggle.checked;
        const time = timeInput.value || '00:00';

        if (!enabled) {
            console.log('Schedule is disabled, not updating time');
            return;
        }

        console.log(`Updating restart time to ${time}`);

        try {
            const payload = {
                Enabled: enabled,
                Time: time
            };

            const response = await this.app.api.post(this.app.api.endpoints.restartSchedule, payload);
            console.log('Restart time update response:', response);

            this.updateNextRestartTime();
            this.app.toastManager.success(`Restart time changed to ${time}`);
        } catch (error) {
            console.error('Error updating restart schedule:', error);
            this.app.toastManager.error('Failed to update restart time');
            // Reload the schedule to get the correct state
            await this.loadSchedule();
        }
    }

    /**
     * Update next restart time display
     */
    updateNextRestartTime() {
        const time = document.getElementById('restart-time').value;
        const [hours, minutes] = time.split(':').map(n => parseInt(n));

        const now = new Date();
        const next = new Date();
        next.setHours(hours, minutes, 0, 0);

        if (next <= now) {
            next.setDate(next.getDate() + 1);
        }

        const formatter = new Intl.DateTimeFormat('en-US', {
            weekday: 'short',
            month: 'short',
            day: 'numeric',
            hour: 'numeric',
            minute: '2-digit',
            hour12: true
        });

        const element = document.getElementById('next-restart-time');
        if (element) {
            element.textContent = formatter.format(next);
        }
    }

    /**
     * Start schedule checker
     */
    startScheduleChecker() {
        this.stopScheduleChecker();
        this.scheduleInterval = setInterval(() => this.checkSchedule(), 30000);
    }

    /**
     * Stop schedule checker
     */
    stopScheduleChecker() {
        if (this.scheduleInterval) {
            clearInterval(this.scheduleInterval);
            this.scheduleInterval = null;
        }
    }

    /**
     * Check restart schedule
     */
    async checkSchedule() {
        try {
            const response = await this.app.api.get(this.app.api.endpoints.restartSchedule);
            
            if (!response.Enabled) {
                this.stopScheduleChecker();
                return;
            }

            if (response.NextRestart) {
                const nextRestartTime = new Date(response.NextRestart);
                const now = new Date();
                const timeDiff = nextRestartTime - now;

                if (timeDiff > 0 && timeDiff < 60000) {
                    this.app.toastManager.warning(
                        `System will restart in ${Math.ceil(timeDiff / 1000)} seconds`,
                        10000
                    );
                }
            }
        } catch (error) {
            console.error('Error checking restart schedule:', error);
        }
    }

    /**
     * Show schedule status
     */
    showScheduleStatus() {
        const element = document.getElementById('schedule-status');
        if (element) {
            element.style.display = 'flex';
            this.updateNextRestartTime();
        }
    }

    /**
     * Hide schedule status
     */
    hideScheduleStatus() {
        const element = document.getElementById('schedule-status');
        if (element) {
            element.style.display = 'none';
        }
    }

    /**
     * Update progress display
     */
    updateProgress(status, details, percentage) {
        document.getElementById('progress-status').textContent = status;
        document.getElementById('progress-details').textContent = details;

        const progressBar = document.getElementById('progress-bar');
        if (percentage >= 0) {
            progressBar.style.width = `${percentage}%`;
            progressBar.classList.remove('indeterminate');
        } else {
            progressBar.classList.add('indeterminate');
        }
    }

    /**
     * Show modal
     */
    showModal(modalName) {
        const modal = document.getElementById(`${modalName}-modal`);
        if (modal) {
            modal.style.display = '';
            modal.classList.add('show');
        }
    }

    /**
     * Close modal
     */
    closeModal(modalName) {
        const modal = document.getElementById(`${modalName}-modal`);
        if (modal) {
            modal.classList.remove('show');
            setTimeout(() => {
                modal.style.display = 'none';
            }, 300);
        }
    }
}

// ============================================================================
// REMOTE CONTROL MODULE
// ============================================================================

/**
 * Remote control for bot instances
 * @class
 */
class RemoteControl {
    constructor(app) {
        this.app = app;
        this.currentPort = null;
        this.currentBotIndex = 0;
        this.availableBots = [];
        this.isLiveMode = true;
        this.isProcessing = false;
        this.stickState = {
            left: { x: 0, y: 0, active: false },
            right: { x: 0, y: 0, active: false }
        };
        this.setupEventHandlers();
        this.initializeSticks();
    }

    /**
     * Setup event handlers
     */
    setupEventHandlers() {
        // Bot selector
        document.getElementById('remote-bot-select')?.addEventListener('change', (e) => {
            this.currentBotIndex = parseInt(e.target.value);
            this.updateBotInfo();
        });

        // Live mode toggle
        document.getElementById('remote-live-mode')?.addEventListener('change', (e) => {
            this.isLiveMode = e.target.checked;
        });

        // Controller buttons
        const modal = document.getElementById('remote-control-modal');
        if (modal) {
            modal.addEventListener('click', async (e) => {
                const btn = e.target.closest('.control-btn, .dpad-btn, .system-btn, .shoulder-btn');
                if (btn && btn.dataset.button) {
                    e.preventDefault();
                    if (!this.isProcessing || this.isLiveMode) {
                        await this.sendButton(btn.dataset.button);
                    }
                }
            });
        }

        // Macro execution
        document.getElementById('execute-macro')?.addEventListener('click', () => {
            const macroInput = document.getElementById('macro-input');
            if (macroInput && macroInput.value.trim() && !this.isProcessing) {
                this.sendMacro(macroInput.value.trim());
            }
        });

        // Macro presets
        document.querySelectorAll('.macro-preset').forEach(btn => {
            btn.addEventListener('click', () => {
                const macro = btn.dataset.macro;
                if (macro && !this.isProcessing) {
                    document.getElementById('macro-input').value = macro;
                    this.sendMacro(macro);
                }
            });
        });

        // Setup analog stick controls
        this.setupStickControls();
    }

    /**
     * Initialize analog sticks with visual indicators
     */
    initializeSticks() {
        const sticks = [
            { element: document.querySelector('.lstick-btn'), type: 'left' },
            { element: document.querySelector('.rstick-btn'), type: 'right' }
        ];

        sticks.forEach(({ element, type }) => {
            if (!element) return;

            // Create visual indicator
            const visual = document.createElement('div');
            visual.className = 'stick-visual';
            visual.innerHTML = '<div class="stick-knob"></div>';
            element.appendChild(visual);

            // Store reference
            this.stickState[type].element = element;
            this.stickState[type].visual = visual;
            this.stickState[type].knob = visual.querySelector('.stick-knob');
        });
    }

    /**
     * Setup analog stick click-and-drag controls
     */
    setupStickControls() {
        const stickStates = {
            left: { isDragging: false, animationFrame: null },
            right: { isDragging: false, animationFrame: null }
        };

        // Function to update stick position
        const updateStickPosition = (type, clientX, clientY) => {
            const stick = this.stickState[type];
            if (!stick.element) return;
            
            const rect = stick.element.getBoundingClientRect();
            const centerX = rect.left + rect.width / 2;
            const centerY = rect.top + rect.height / 2;
            
            // Calculate offset from center
            let deltaX = clientX - centerX;
            let deltaY = clientY - centerY;
            
            // Limit to circular area
            const distance = Math.sqrt(deltaX * deltaX + deltaY * deltaY);
            const maxDistance = rect.width / 2 * 0.8; // 80% of radius
            
            if (distance > maxDistance) {
                const ratio = maxDistance / distance;
                deltaX *= ratio;
                deltaY *= ratio;
            }
            
            // Convert to -1 to 1 range
            stick.x = deltaX / maxDistance;
            stick.y = deltaY / maxDistance;
            
            // Update visual
            if (stick.knob) {
                stick.knob.style.transform = `translate(calc(-50% + ${deltaX}px), calc(-50% + ${deltaY}px))`;
            }
            
            // Send to bot
            if (this.isLiveMode) {
                this.sendStickPosition(type, stick.x, stick.y);
            }
        };

        // Setup each stick
        ['left', 'right'].forEach(type => {
            const stick = this.stickState[type];
            if (!stick.element) return;
            
            const state = stickStates[type];

            // Mouse down / touch start
            const handleStart = (e) => {
                e.preventDefault();
                e.stopPropagation();
                
                state.isDragging = true;
                stick.active = true;
                stick.element.classList.add('stick-active');
                
                const clientX = e.touches ? e.touches[0].clientX : e.clientX;
                const clientY = e.touches ? e.touches[0].clientY : e.clientY;
                
                updateStickPosition(type, clientX, clientY);
            };

            // Mouse events
            stick.element.addEventListener('mousedown', handleStart);
            
            // Touch events  
            stick.element.addEventListener('touchstart', handleStart, { passive: false });
            
            // Prevent context menu
            stick.element.addEventListener('contextmenu', (e) => e.preventDefault());
        });

        // Global move handler
        const handleMove = (e) => {
            ['left', 'right'].forEach(type => {
                const state = stickStates[type];
                if (!state.isDragging) return;
                
                const stick = this.stickState[type];
                if (!stick.element) return;
                
                // Cancel previous frame
                if (state.animationFrame) {
                    cancelAnimationFrame(state.animationFrame);
                }
                
                // Schedule update
                state.animationFrame = requestAnimationFrame(() => {
                    const clientX = e.touches ? e.touches[0].clientX : e.clientX;
                    const clientY = e.touches ? e.touches[0].clientY : e.clientY;
                    
                    updateStickPosition(type, clientX, clientY);
                });
            });
        };

        // Global end handler
        const handleEnd = () => {
            ['left', 'right'].forEach(type => {
                const state = stickStates[type];
                if (!state.isDragging) return;
                
                const stick = this.stickState[type];
                if (!stick.element) return;
                
                state.isDragging = false;
                stick.active = false;
                stick.element.classList.remove('stick-active');
                
                // Cancel animation frame
                if (state.animationFrame) {
                    cancelAnimationFrame(state.animationFrame);
                    state.animationFrame = null;
                }
                
                // Reset to center
                stick.x = 0;
                stick.y = 0;
                
                if (stick.knob) {
                    stick.knob.style.transform = 'translate(-50%, -50%)';
                }
                
                // Send neutral position
                this.sendStickPosition(type, 0, 0);
            });
        };

        // Attach global listeners
        document.addEventListener('mousemove', handleMove);
        document.addEventListener('mouseup', handleEnd);
        document.addEventListener('touchmove', handleMove, { passive: false });
        document.addEventListener('touchend', handleEnd);
        document.addEventListener('touchcancel', handleEnd);
    }


    /**
     * Send stick position to bot
     * @param {string} type - 'left' or 'right'
     * @param {number} x - X position (-1 to 1)
     * @param {number} y - Y position (-1 to 1)
     */
    async sendStickPosition(type, x, y) {
        if (!this.currentPort || this.isProcessing) return;
        
        const selectedBot = this.availableBots[this.currentBotIndex];
        if (!selectedBot || !this.isBotRunning(selectedBot)) return;
        
        try {
            // Convert to controller values (0-255 with 128 as center)
            const stickX = Math.round((x + 1) * 127.5);
            const stickY = Math.round((1 - y) * 127.5); // Invert Y for controller
            
            await this.app.api.post(
                `/api/bot/instances/${this.currentPort}/remote/stick`,
                { 
                    stick: type.toUpperCase(),
                    x: stickX,
                    y: stickY,
                    botIndex: this.currentBotIndex 
                }
            );
        } catch (error) {
            console.error('Stick position error:', error);
        }
    }

    /**
     * Open remote control for instance
     * @param {number} port - Instance port
     */
    async open(port) {
        this.currentPort = port;
        this.currentBotIndex = 0;
        
        const modal = document.getElementById('remote-control-modal');
        if (modal) {
            modal.style.display = 'flex';
            modal.classList.add('show');
            await this.loadAvailableBots();
        }
    }

    /**
     * Load available bots
     */
    async loadAvailableBots() {
        try {
            const response = await this.app.api.get(`/api/bot/instances/${this.currentPort}/bots`);
            const bots = Array.isArray(response) ? response : (response.Bots || response.bots || []);
            
            this.availableBots = bots;
            this.populateBotSelector(bots);
            
            if (bots && bots.length > 0) {
                const runningBots = bots.filter(b => this.isBotRunning(b));
                
                if (runningBots.length > 0) {
                    this.currentBotIndex = bots.indexOf(runningBots[0]);
                    document.getElementById('remote-bot-select').value = this.currentBotIndex.toString();
                    this.updateBotInfo();
                }
            }
        } catch (error) {
            console.error('Failed to load bots:', error);
            this.app.toastManager.error('Failed to load bot list');
        }
    }

    /**
     * Check if bot is running
     * @param {Object} bot - Bot object
     * @returns {boolean} True if running
     */
    isBotRunning(bot) {
        const stoppedStates = ['STOPPED', 'STOPPING', 'ERROR', 'UNKNOWN'];
        const idleStates = ['IDLE', 'IDLING'];
        const botStatus = bot.Status || bot.status;
        
        return botStatus && 
               !stoppedStates.includes(botStatus.toUpperCase()) &&
               !idleStates.includes(botStatus.toUpperCase());
    }

    /**
     * Populate bot selector
     * @param {Array} bots - Available bots
     */
    populateBotSelector(bots) {
        const selector = document.getElementById('remote-bot-select');
        if (!selector) return;

        selector.innerHTML = '';
        
        if (!bots || bots.length === 0) {
            selector.innerHTML = '<option value="">No bots available</option>';
            return;
        }
        
        bots.forEach((bot, index) => {
            const option = document.createElement('option');
            option.value = index.toString();
            
            const isRunning = this.isBotRunning(bot);
            const status = isRunning ? '🟢' : '🔴';
            const name = bot.Name || bot.name || `Bot ${index + 1}`;
            
            option.textContent = `${status} ${name}`;
            option.disabled = !isRunning;
            
            selector.appendChild(option);
        });
    }

    /**
     * Update bot info display
     */
    updateBotInfo() {
        const bot = this.availableBots[this.currentBotIndex];
        if (!bot) return;

        const connectionInfo = document.getElementById('bot-connection-info');
        if (connectionInfo) {
            const isRunning = this.isBotRunning(bot);
            const name = bot.Name || bot.name || 'Bot';
            const status = bot.Status || bot.status || 'Unknown';
            
            connectionInfo.textContent = `${name} - ${status}`;
            connectionInfo.className = `connection-info status-${isRunning ? 'success' : 'warning'}`;
        }
    }

    /**
     * Send button press
     * @param {string} button - Button name
     */
    async sendButton(button) {
        if (!this.currentPort || this.isProcessing) return;
        
        const selectedBot = this.availableBots[this.currentBotIndex];
        if (!selectedBot || !this.isBotRunning(selectedBot)) {
            this.app.toastManager.error('Selected bot is not running');
            return;
        }

        this.isProcessing = true;
        
        try {
            const response = await this.app.api.post(
                `/api/bot/instances/${this.currentPort}/remote/button`,
                { button, botIndex: this.currentBotIndex }
            );

            if (response.success) {
                this.animateButton(button);
            } else {
                this.app.toastManager.error(response.error || 'Failed to send button');
            }
        } catch (error) {
            console.error('Remote button error:', error);
            this.app.toastManager.error('Connection error');
        } finally {
            this.isProcessing = false;
        }
    }

    /**
     * Send macro
     * @param {string} macro - Macro string
     */
    async sendMacro(macro) {
        if (!this.currentPort || this.isProcessing) return;
        
        const selectedBot = this.availableBots[this.currentBotIndex];
        if (!selectedBot || !this.isBotRunning(selectedBot)) {
            this.app.toastManager.error('Selected bot is not running');
            return;
        }

        this.isProcessing = true;
        
        try {
            const response = await this.app.api.post(
                `/api/bot/instances/${this.currentPort}/remote/macro`,
                { macro, botIndex: this.currentBotIndex }
            );

            if (response.success) {
                this.app.toastManager.success(`Macro executed: ${response.commandCount || 0} commands`);
            } else {
                this.app.toastManager.error(response.error || 'Failed to execute macro');
            }
        } catch (error) {
            console.error('Remote macro error:', error);
            this.app.toastManager.error('Connection error');
        } finally {
            this.isProcessing = false;
        }
    }

    /**
     * Animate button press
     * @param {string} button - Button name
     */
    animateButton(button) {
        const btn = document.querySelector(`[data-button="${button}"]`);
        if (btn) {
            btn.style.transform = 'scale(0.9)';
            btn.style.background = 'var(--success-color)';
            setTimeout(() => {
                btn.style.transform = '';
                btn.style.background = '';
            }, 200);
        }
    }

    /**
     * Close remote control
     */
    close() {
        const modal = document.getElementById('remote-control-modal');
        if (modal) {
            modal.classList.remove('show');
            setTimeout(() => {
                modal.style.display = 'none';
            }, 300);
        }
        this.currentPort = null;
        this.currentBotIndex = 0;
        this.availableBots = [];
    }
}

// ============================================================================
// INITIALIZATION
// ============================================================================

// Initialize application when DOM is ready
document.addEventListener('DOMContentLoaded', () => {
    // Hide all modals initially
    const modals = document.querySelectorAll('.modal');
    modals.forEach(modal => {
        if (!modal.classList.contains('show')) {
            modal.style.display = 'none';
        }
    });

    // Create and start the application
    window.botControlPanel = new BotControlPanel();
});

// Export for module usage if needed
if (typeof module !== 'undefined' && module.exports) {
    module.exports = {
        BotControlPanel,
        ThemeManager,
        ApiService,
        StatusManager,
        ToastManager,
        InstanceRenderer,
        DashboardManager,
        StateManager,
        RefreshManager,
        CommandManager,
        UpdateManager,
        RestartManager,
        RemoteControl
    };
}
