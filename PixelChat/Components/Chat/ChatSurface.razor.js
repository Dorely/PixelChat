export function attachScroller(element) {
    if (!element) return;
    element.dataset.chatAutoFollow = 'true';
    element.addEventListener('scroll', () => {
        const distance = element.scrollHeight - element.scrollTop - element.clientHeight;
        element.dataset.chatAutoFollow = distance < 80 ? 'true' : 'false';
    });
}

export function attachAutoSize(textarea) {
    if (!textarea) return;
    const resize = () => {
        textarea.style.height = 'auto';
        textarea.style.height = Math.min(textarea.scrollHeight, 180) + 'px';
    };
    textarea.addEventListener('input', resize);
    resize();
}

const allowedImageTypes = new Set(['image/png', 'image/jpeg']);

export function attachComposer(textarea, dotNetRef) {
    if (!textarea) return;
    textarea.addEventListener('keydown', event => {
        if (event.key === 'Enter' && !event.shiftKey) {
            const button = textarea.closest('.chat-surface')?.querySelector('[data-chat-send="true"]');
            if (button && !button.disabled) {
                event.preventDefault();
                button.click();
            }
        }
    });
    textarea.addEventListener('paste', event => handlePaste(event, dotNetRef));
}

export function resetComposer(textarea) {
    if (!textarea) return;
    textarea.value = '';
    textarea.style.height = 'auto';
}

export function scrollToBottom(element, force) {
    if (!element) return;
    if (force || element.dataset.chatAutoFollow !== 'false') {
        element.scrollTop = element.scrollHeight;
    }
}

async function handlePaste(event, dotNetRef) {
    if (!dotNetRef || !event.clipboardData) return;

    const imageFiles = Array.from(event.clipboardData.files || [])
        .filter(file => file.type?.startsWith('image/'));
    if (imageFiles.length === 0) return;

    event.preventDefault();
    for (let index = 0; index < imageFiles.length; index++) {
        const file = imageFiles[index];
        const normalizedType = normalizeImageType(file.type);
        if (!allowedImageTypes.has(normalizedType)) {
            await rejectPaste(dotNetRef, 'Only PNG and JPEG images can be pasted into chat.');
            continue;
        }

        try {
            const imageInfo = await buildPreview(file, normalizedType);
            const fileName = file.name || `clipboard-image-${timestamp()}-${index + 1}.${extensionForType(normalizedType)}`;
            await dotNetRef.invokeMethodAsync(
                'OnChatImagePasted',
                fileName,
                normalizedType,
                file.size,
                imageInfo.width,
                imageInfo.height,
                imageInfo.previewDataUrl,
                DotNet.createJSStreamReference(file));
        } catch (error) {
            await rejectPaste(dotNetRef, error?.message || 'Could not paste the clipboard image.');
        }
    }
}

async function rejectPaste(dotNetRef, message) {
    try {
        await dotNetRef.invokeMethodAsync('OnChatPasteRejected', message);
    } catch {
    }
}

async function buildPreview(file, contentType) {
    if (window.createImageBitmap) {
        try {
            const bitmap = await createImageBitmap(file);
            const previewDataUrl = drawPreview(bitmap, contentType);
            const width = bitmap.width;
            const height = bitmap.height;
            bitmap.close?.();
            return { width, height, previewDataUrl };
        } catch {
        }
    }

    return await buildPreviewFromImage(file, contentType);
}

function drawPreview(source, contentType) {
    const maxDimension = 128;
    const width = source.width || source.naturalWidth || maxDimension;
    const height = source.height || source.naturalHeight || maxDimension;
    const scale = Math.min(1, maxDimension / Math.max(width, height));
    const targetWidth = Math.max(1, Math.round(width * scale));
    const targetHeight = Math.max(1, Math.round(height * scale));
    const canvas = document.createElement('canvas');
    canvas.width = targetWidth;
    canvas.height = targetHeight;
    const ctx = canvas.getContext('2d');
    ctx.clearRect(0, 0, targetWidth, targetHeight);
    ctx.drawImage(source, 0, 0, targetWidth, targetHeight);
    return canvas.toDataURL(contentType === 'image/jpeg' ? 'image/jpeg' : 'image/png', 0.86);
}

function buildPreviewFromImage(file, contentType) {
    const url = URL.createObjectURL(file);
    return new Promise((resolve, reject) => {
        const image = new Image();
        image.onload = () => {
            try {
                resolve({
                    width: image.naturalWidth || image.width || null,
                    height: image.naturalHeight || image.height || null,
                    previewDataUrl: drawPreview(image, contentType),
                });
            } catch (error) {
                reject(error);
            } finally {
                URL.revokeObjectURL(url);
            }
        };
        image.onerror = () => {
            URL.revokeObjectURL(url);
            reject(new Error('Could not read the pasted image.'));
        };
        image.src = url;
    });
}

function normalizeImageType(value) {
    const normalized = String(value || '').trim().toLowerCase();
    return normalized === 'image/jpg' ? 'image/jpeg' : normalized;
}

function extensionForType(contentType) {
    return contentType === 'image/jpeg' ? 'jpg' : 'png';
}

function timestamp() {
    const now = new Date();
    const pad = value => String(value).padStart(2, '0');
    return `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;
}
