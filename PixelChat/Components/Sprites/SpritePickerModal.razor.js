const dialogs = new WeakMap();
export function open(dialog, dotnet) {
    const previousFocus = document.activeElement;
    const cancel = e => { e.preventDefault(); dotnet.invokeMethodAsync('CloseAsync'); };
    const click = e => {
        if (e.target !== dialog) return;
        const r = dialog.getBoundingClientRect();
        if (e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom)
            dotnet.invokeMethodAsync('CloseAsync');
    };
    dialogs.set(dialog, { cancel, click, previousFocus });
    dialog.addEventListener('cancel', cancel);
    dialog.addEventListener('click', click);
    dialog.showModal();
}
export function dispose(dialog) {
    const state = dialogs.get(dialog);
    if (!state) return;
    dialog.removeEventListener('cancel', state.cancel);
    dialog.removeEventListener('click', state.click);
    dialog.close();
    if (state.previousFocus?.isConnected) state.previousFocus.focus({ preventScroll: true });
    dialogs.delete(dialog);
}
