/* Header notification bell — shared between the Admin and Home layouts.
   Polls /api/notifications (scoped server-side to the caller's role/store
   access) and renders a dropdown panel positioned relative to the bell
   button itself, so it works regardless of what each layout's header
   markup looks like or clips.

   Notifications themselves are computed live server-side every call (no
   read/unread state, no history table) — "clearing" here is a purely
   client-side dismissal: cleared items are hashed into localStorage and
   hidden from this browser until the underlying condition actually
   changes (the item's key stops appearing in a fresh fetch), at which
   point its dismissal is dropped automatically so a *new* occurrence of
   the same type/store/title shows up again. */
(function () {
    const btn = document.getElementById('notifBtn');
    const badge = document.getElementById('notifBadge');
    const panel = document.getElementById('notifPanel');
    const list = document.getElementById('notifList');
    const clearBtn = document.getElementById('notifClearBtn');
    if (!btn || !panel || !list) return;

    const areaPrefix = document.body.dataset.areaPrefix || '';
    const DISMISSED_KEY = 'mf-notif-dismissed';

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

    function itemKey(i) {
        return `${i.type}::${i.store || ''}::${i.title}`;
    }

    function loadDismissed() {
        try {
            const raw = localStorage.getItem(DISMISSED_KEY);
            return raw ? new Set(JSON.parse(raw)) : new Set();
        } catch {
            return new Set();
        }
    }

    function saveDismissed(set) {
        try {
            localStorage.setItem(DISMISSED_KEY, JSON.stringify([...set]));
        } catch { /* private browsing / storage disabled — dismissal just won't persist */ }
    }

    function linkFor(item) {
        if (item.type === 'high-risk') return `${areaPrefix}/dashboard/earlywarning`;
        if (item.store) return `${areaPrefix}/dashboard/actioncenterdetail?store=${encodeURIComponent(item.store)}`;
        return `${areaPrefix}/dashboard/actioncenter`;
    }

    let visibleItems = [];

    function render(items) {
        visibleItems = items;
        badge.hidden = items.length === 0;
        badge.textContent = items.length > 99 ? '99+' : String(items.length);
        if (clearBtn) clearBtn.hidden = items.length === 0;

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

    async function fetchNotifications() {
        let items;
        try {
            const res = await fetch('/api/notifications');
            if (!res.ok) return;
            items = await res.json();
        } catch {
            return;
        }

        // Drop any dismissal whose underlying notification is no longer live —
        // keeps localStorage from growing forever and lets a resolved-then-
        // recurring issue notify again instead of staying silently hidden.
        const liveKeys = new Set(items.map(itemKey));
        const dismissed = loadDismissed();
        let dismissedChanged = false;
        for (const k of [...dismissed]) {
            if (!liveKeys.has(k)) { dismissed.delete(k); dismissedChanged = true; }
        }
        if (dismissedChanged) saveDismissed(dismissed);

        render(items.filter(i => !dismissed.has(itemKey(i))));
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

    if (clearBtn) {
        clearBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            const dismissed = loadDismissed();
            visibleItems.forEach(i => dismissed.add(itemKey(i)));
            saveDismissed(dismissed);
            render([]);
        });
    }

    fetchNotifications();
    setInterval(fetchNotifications, 60000);
})();
