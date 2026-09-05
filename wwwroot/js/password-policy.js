// Live password-requirements checklist: ticks off each rule (12+ chars,
// uppercase, lowercase, digit, symbol) as the user types, mirroring
// PasswordPolicy (Services/PasswordPolicy.cs) on the server.
function initPasswordChecklist(inputId, listId) {
    const input = document.getElementById(inputId);
    const list = document.getElementById(listId);
    if (!input || !list) return;

    const items = Array.from(list.querySelectorAll('[data-rule]'));

    function evaluate() {
        const v = input.value || '';
        const checks = {
            length: v.length >= 12,
            upper: /[A-Z]/.test(v),
            lower: /[a-z]/.test(v),
            digit: /[0-9]/.test(v),
            symbol: /[^A-Za-z0-9]/.test(v),
        };
        items.forEach(li => {
            const ok = !!checks[li.dataset.rule];
            li.classList.toggle('pwck-ok', ok);
            const icon = li.querySelector('.pwck-icon');
            if (icon) icon.className = 'pwck-icon bi ' + (ok ? 'bi-check-circle-fill' : 'bi-circle');
        });
    }

    input.addEventListener('input', evaluate);
    evaluate();
}
