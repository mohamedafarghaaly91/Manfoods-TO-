/* Shared chart color system.
   Semantic colors = business meaning (good/warn/bad/neutral).
   Categorical colors = no good/bad meaning; always looked up by LABEL,
   never by array position, so the same category is always the same color. */

const ChartColors = (function () {
    const SEMANTIC = {
        GOOD:           'oklch(0.62 0.15 155)', // green
        WARNING:        'oklch(0.75 0.16 75)',  // amber
        WARNING_STRONG: 'oklch(0.68 0.18 55)',  // orange (stronger warning tier, same family as WARNING)
        BAD:            'oklch(0.6 0.22 22)',   // red
        NEUTRAL:        'oklch(0.5 0.03 258)',  // gray/blue
    };

    // Categorical palette: deliberately clear of the reds/greens used above,
    // so a category chart can never read as a good/bad signal.
    const CATEGORICAL_PALETTE = [
        'oklch(0.65 0.15 190)', // teal
        'oklch(0.6 0.16 280)',  // violet
        'oklch(0.55 0.15 258)', // blue
        'oklch(0.68 0.15 310)', // magenta
        'oklch(0.72 0.17 60)',  // gold
        'oklch(0.62 0.18 15)',  // brick
        'oklch(0.66 0.14 230)', // steel blue
        'oklch(0.6 0.02 258)',  // slate
    ];

    // Deterministic fallback for labels with no fixed mapping (e.g. free-text
    // resignation reasons): same label always hashes to the same palette slot.
    function hashColor(label) {
        const s = String(label ?? '');
        let hash = 0;
        for (let i = 0; i < s.length; i++) hash = (hash * 31 + s.charCodeAt(i)) | 0;
        return CATEGORICAL_PALETTE[Math.abs(hash) % CATEGORICAL_PALETTE.length];
    }

    const GENDER_COLORS = {
        'Male':   CATEGORICAL_PALETTE[0],
        'Female': CATEGORICAL_PALETTE[1],
        'Other':  CATEGORICAL_PALETTE[2],
    };

    const RETENTION_MILESTONE_COLORS = {
        '6 Months': CATEGORICAL_PALETTE[0],
        '1 Year':   CATEGORICAL_PALETTE[1],
        '2 Years':  CATEGORICAL_PALETTE[2],
        '3 Years':  CATEGORICAL_PALETTE[3],
        '4 Years':  CATEGORICAL_PALETTE[4],
        '5 Years':  CATEGORICAL_PALETTE[5],
    };

    const RETENTION_TENURE_BUCKET_COLORS = {
        '< 6 months':   CATEGORICAL_PALETTE[0],
        '6–12 months':  CATEGORICAL_PALETTE[1],
        '1–2 years':    CATEGORICAL_PALETTE[2],
        '2–3 years':    CATEGORICAL_PALETTE[3],
        '3–4 years':    CATEGORICAL_PALETTE[4],
        '4–5 years':    CATEGORICAL_PALETTE[5],
        '5+ years':     CATEGORICAL_PALETTE[6],
    };

    const EARLY_WARNING_REASON_COLORS = {
        new_hire_window:      CATEGORICAL_PALETTE[0],
        store_history:        CATEGORICAL_PALETTE[1],
        role_history:         CATEGORICAL_PALETTE[2],
        peak_window:          CATEGORICAL_PALETTE[3],
        gender_history:       CATEGORICAL_PALETTE[4],
        exit_interview_score: CATEGORICAL_PALETTE[5],
        store_leader_history: CATEGORICAL_PALETTE[6],
    };

    // labels: string[] -> colors: string[], aligned by LABEL via knownMap,
    // falling back to a stable hash color for anything not in knownMap.
    function colorsByLabel(labels, knownMap) {
        return labels.map(label => (knownMap && knownMap[label]) || hashColor(label));
    }

    return {
        SEMANTIC,
        CATEGORICAL_PALETTE,
        GENDER_COLORS,
        RETENTION_MILESTONE_COLORS,
        RETENTION_TENURE_BUCKET_COLORS,
        EARLY_WARNING_REASON_COLORS,
        colorsByLabel,
        hashColor,
    };
})();
