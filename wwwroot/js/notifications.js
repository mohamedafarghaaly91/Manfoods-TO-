/* Header notification bell — shared between the Admin and Home layouts.
   Polls /api/notifications (scoped server-side to the caller's role/store
   access) and renders a dropdown panel positioned relative to the bell
   button itself, so it works regardless of what each layout's header
   markup looks like or clips. */
(function () {
    const btn = document.getElementById('notifBtn');
    const badge = document.getElementById('notifBadge');
    const panel = document.getElementById('notifPanel');
    const list = document.getElementById('notifList');
    if (!btn || !panel || !list) return;

    const areaPrefix = document.body.dataset.areaPrefix || '';

    const typeIcons = {
        critical: '<i class="bi bi-exclamation-octagon-fill" style="color:var(--danger)"></i>',
        stalled: '<i class="bi bi-hourglass-split" style="color:var(--warning)"></i>',
        overdue: '<i class="bi bi-calendar-x-fill" style="color:var(--danger)"></i>',
        'high-risk': '<i class="bi bi-shield-exclamation" style="color:var(--warning)"></i>',
    };

    function esc(s) {
        const d = document.createElement('div');
        d.textContent = s ?? '';
        return d.innerHTML;
    }

    function linkFor(item) {
        if (item.type === 'high-risk') return `${areaPrefix}/dashboard/earlywarning`;
        if (item.store) return `${areaPrefix}/dashboard/actioncenterdetail?store=${encodeURIComponent(item.store)}`;
        return `${areaPrefix}/dashboard/actioncenter`;
    }

    async function fetchNotifications() {
        let items;
        try {
            const res = await fetch('/api/notifications');
            if (!res.ok) return;
            items = await res.json();
        } catch {
            return;
        }

        badge.hidden = items.length === 0;
        badge.textContent = items.length > 99 ? '99+' : String(items.length);

        if (items.length === 0) {
            list.innerHTML = `<div class="notif-empty">${esc(btn.dataset.emptyText || '')}</div>`;
            return;
        }

        list.innerHTML = items.map(i => `
            <a class="notif-item" href="${linkFor(i)}">
                <div class="notif-item-icon">${typeIcons[i.type] || ''}</div>
                <div class="notif-item-body">
                    <div class="notif-item-title">${esc(i.title)}</div>
                    <div class="notif-item-desc">${esc(i.description)}</div>
                </div>
            </a>
        `).join('');
    }

    function positionPanel() {
        const r = btn.getBoundingClientRect();
        const isRtl = document.documentElement.getAttribute('dir') === 'rtl';
        panel.style.top = Math.round(r.bottom + 8) + 'px';
        if (isRtl) {
            panel.style.left = Math.round(r.left) + 'px';
            panel.style.right = 'auto';
        } else {
            panel.style.right = Math.round(window.innerWidth - r.right) + 'px';
            panel.style.left = 'auto';
        }
    }

    btn.addEventListener('click', (e) => {
        e.stopPropagation();
        panel.hidden = !panel.hidden;
        if (!panel.hidden) {
            positionPanel();
            fetchNotifications();
        }
    });
    document.addEventListener('click', (e) => {
        if (!panel.hidden && !panel.contains(e.target) && !btn.contains(e.target)) panel.hidden = true;
    });
    window.addEventListener('resize', () => { if (!panel.hidden) positionPanel(); });

    fetchNotifications();
    setInterval(fetchNotifications, 60000);
})();
