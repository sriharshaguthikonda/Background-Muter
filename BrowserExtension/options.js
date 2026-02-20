// Background Muter - Options Page Script

const DEFAULT_SETTINGS = {
    pauseOnTabSwitch: true,
    pauseOnWindowSwitch: true,
    autoPlayOnWindowFocus: true
};

// Load settings from storage
async function loadSettings() {
    const result = await chrome.storage.sync.get(DEFAULT_SETTINGS);
    
    document.getElementById('pauseOnTabSwitch').checked = result.pauseOnTabSwitch;
    document.getElementById('pauseOnWindowSwitch').checked = result.pauseOnWindowSwitch;
    document.getElementById('autoPlayOnWindowFocus').checked = result.autoPlayOnWindowFocus;
}

// Save settings to storage
async function saveSettings() {
    const settings = {
        pauseOnTabSwitch: document.getElementById('pauseOnTabSwitch').checked,
        pauseOnWindowSwitch: document.getElementById('pauseOnWindowSwitch').checked,
        autoPlayOnWindowFocus: document.getElementById('autoPlayOnWindowFocus').checked
    };
    
    await chrome.storage.sync.set(settings);
    
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
