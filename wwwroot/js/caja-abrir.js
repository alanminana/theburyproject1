document.addEventListener('DOMContentLoaded', () => {
    const toast = document.getElementById('toast-error');
    if (toast) {
        setTimeout(() => {
            toast.style.transition = 'opacity 0.5s';
            toast.style.opacity = '0';
            setTimeout(() => toast.remove(), 500);
        }, 5000);
    }
});
