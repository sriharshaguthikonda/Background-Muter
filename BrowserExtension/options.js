// Background Muter - Options Page Script

const DEFAULT_SETTINGS = {
    pauseOnTabSwitch: true,
    pauseOnWindowSwitch: true,
    autoPlayOnWindowFocus: true
};

async function readSettings() {
    const [syncResult, localResult] = await Promise.all([
        chrome.storage.sync.get(null),
        chrome.storage.local.get(null)
    ]);

    // Prefer sync when available, but fall back to local when sync is empty/disabled.
    return { ...DEFAULT_SETTINGS, ...localResult, ...syncResult };
}

// Load settings from storage
async function loadSettings() {
    const result = await readSettings();

    const pauseOnTabSwitch = document.getElementById('pauseOnTabSwitch');
    const pauseOnWindowSwitch = document.getElementById('pauseOnWindowSwitch');
    const autoPlayOnWindowFocus = document.getElementById('autoPlayOnWindowFocus');

    // Window switch pausing is mandatory; tab switch pausing is disabled.
    pauseOnTabSwitch.checked = false;
    pauseOnTabSwitch.disabled = true;
    pauseOnWindowSwitch.checked = true;
    pauseOnWindowSwitch.disabled = true;

    autoPlayOnWindowFocus.checked = result.autoPlayOnWindowFocus;
}

// Save settings to storage
async function saveSettings() {
    const settings = {
        pauseOnTabSwitch: false,
        pauseOnWindowSwitch: true,
        autoPlayOnWindowFocus: document.getElementById('autoPlayOnWindowFocus').checked
    };
    
    await Promise.all([
        chrome.storage.sync.set(settings),
        chrome.storage.local.set(settings)
    ]);
    
    // Notify background script of settings change
    chrome.runtime.sendMessage({ type: 'settingsChanged', settings });
    
    // Show saved status
    const status = document.getElementById('status');
    status.textContent = 'Settings saved!';
    status.classList.add('success');
    setTimeout(() => {
        status.classList.remove('success');
    }, 2000);
}

// Initialize
document.addEventListener('DOMContentLoaded', loadSettings);

// Add change listeners to all checkboxes
document.querySelectorAll('input[type="checkbox"]').forEach(checkbox => {
    checkbox.addEventListener('change', saveSettings);
});
