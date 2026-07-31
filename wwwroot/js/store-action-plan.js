/* Shared helpers for the Store Action Plan pages — Admin & Home areas.
   Used by _StoreActionPlansContent.cshtml and _StoreActionPlanDetailContent.cshtml.
   Kept separate from dashboard.js since neither page needs its
   period-selector/Chart.js machinery. */

function sapEscape(s) {
    if (s === null || s === undefined) return '';
    const div = document.createElement('div');
    div.textContent = String(s);
    return div.innerHTML;
}

async function sapFetchJson(url) {
    const r = await fetch(url);
    return r.ok ? r.json() : null;
}

const SAP_TONE_VAR = { success: 'oklch(0.62 0.15 155)', warning: 'oklch(0.75 0.16 75)', neutral: 'var(--text-3)' };
const SAP_TONE_BG  = { success: 'oklch(0.62 0.15 155 / .1)', warning: 'oklch(0.75 0.16 75 / .15)', neutral: 'var(--card-bg-2)' };

/* status: 'Active' | 'Resolved' | anything else (no plan).
   labels: { active, resolved, none } — localized text supplied by the caller,
   read from that page's own data-* attributes. */
function sapStatusMeta(status, labels) {
    if (status === 'Active')   return { label: labels.active, tone: 'warning' };
    if (status === 'Resolved') return { label: labels.resolved, tone: 'success' };
    return { label: labels.none, tone: 'neutral' };
}
