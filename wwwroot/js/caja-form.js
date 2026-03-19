document.addEventListener('DOMContentLoaded', () => {
    // Auto-dismiss toasts
    document.querySelectorAll('[id^="toast-"]').forEach(toast => {
        setTimeout(() => {
            toast.style.transition = 'opacity .4s ease, transform .4s ease';
            toast.style.opacity = '0';
            toast.style.transform = 'translateY(-8px)';
            setTimeout(() => toast.remove(), 400);
        }, 5000);
    });
});
