import { initViewer, loadModel } from './viewer.js';

initViewer(document.getElementById('preview')).then(viewer => {
    const urn = window.location.hash?.substring(1);
    setupModelSelection(viewer, urn);
    setupModelUpload(viewer);
});

async function setupModelSelection(viewer, selectedUrn) {
    const dropdown = document.getElementById('models');
    dropdown.innerHTML = '';
    try {
        const resp = await fetch('/api/models');
        if (!resp.ok) {
            throw new Error(await resp.text());
        }
        const models = await resp.json();
        console.log(models); // <-- Print models to the console
        dropdown.innerHTML = models.map(model => `<option value=${model.urn} ${model.urn === selectedUrn ? 'selected' : ''}>${model.name}</option>`).join('\n');
        dropdown.onchange = () => onModelSelected(viewer, dropdown.value);
        if (dropdown.value) {
            onModelSelected(viewer, dropdown.value);
        }
    } catch (err) {
        alert('Could not list models. See the console for more details.');
        console.error(err);
    }
}


async function setupModelUpload(viewer) {
    const upload = document.getElementById('upload');
    const input = document.getElementById('input');
    const models = document.getElementById('models');
    upload.onclick = () => input.click();
    input.onchange = async () => {
        const file = input.files[0];
        if (!file) return;

        let data = new FormData();
        data.append('model-file', file);

        // ✅ Only ask for entrypoint if ZIP
        if (file.name.toLowerCase().endsWith('.zip')) {
            let entrypoint = null;
            while (!entrypoint) {
                entrypoint = window.prompt('Please enter the filename of the main design inside the archive (required):');
                if (entrypoint === null) {
                    // User cancelled, abort upload
                    input.value = '';
                    return;
                }
                entrypoint = entrypoint.trim();
            }
            data.append('model-zip-entrypoint', entrypoint);
        }

        // Debug: Show what is being sent
        console.log('Uploading file:', file.name, 'FormData:', Array.from(data.entries()));

        try {
            const resp = await fetch('/api/models', {
                method: 'POST',
                body: data
            });
            if (!resp.ok) {
                throw new Error(await resp.text());
            }
            alert('Upload successful!');
            setupModelSelection(viewer); // Refresh model list
        } catch (err) {
            alert('Upload failed. See the console for more details.');
            console.error(err);
        } finally {
            input.value = ''; // Reset file input
        }
    };
}





async function onModelSelected(viewer, urn) {
    if (window.onModelSelectedTimeout) {
        clearTimeout(window.onModelSelectedTimeout);
        delete window.onModelSelectedTimeout;
    }
    window.location.hash = urn;
    try {
        const resp = await fetch(`/api/models/${urn}/status`);
        if (!resp.ok) {
            throw new Error(await resp.text());
        }


        const status = await resp.json();

        console.log('Translation status:', status);
        switch (status.status) {
            case 'n/a':
                showNotification(`Model has not been translated.`);
                break;
            case 'inprogress':
                showNotification(`Model is being translated (${status.progress})...`);
                window.onModelSelectedTimeout = setTimeout(onModelSelected, 5000, viewer, urn);
                break;
            case 'failed':
                showNotification(`Translation failed. <ul>${status.messages.map(msg => `<li>${JSON.stringify(msg)}</li>`).join('')}</ul>`);
                break;

            default:
                clearNotification();
                loadModel(viewer, urn);
                break;
        }
    } catch (err) {
        alert('Could not load model. See the console for more details.');
        console.error(err);
    }
}

function showNotification(message) {
    const overlay = document.getElementById('overlay');
    overlay.innerHTML = `<div class="notification">${message}</div>`;
    overlay.style.display = 'flex';
}

function clearNotification() {
    const overlay = document.getElementById('overlay');
    overlay.innerHTML = '';
    overlay.style.display = 'none';
}

document.getElementById('addLineBtn').onclick = async () => {
    try {
        const resp = await fetch('http://localhost:12345/addLine', { method: 'GET' });
        const text = await resp.text();
        alert(text);
    } catch {
        alert('Failed to connect to AutoCAD local server.');
    }
};