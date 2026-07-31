/* Shared chart color system.
   Semantic colors = business meaning (good/warn/bad/neutral).
   Categorical colors = no good/bad meaning; always looked up by LABEL,
   never by array position, so the same category is always the same color. */

const ChartColors = (function () {
    const SEMANTIC = {
        GOOD:           'oklch(0.62 0.15 155)', // green
        GOOD_LIGHT:     'oklch(0.75 0.13 145)', // light green (Likert level 4)
        WARNING:        'oklch(0.75 0.16 75)',  // amber
        WARNING_STRONG: 'oklch(0.68 0.18 55)',  // orange (stronger warning tier, same family as WARNING)
        BAD:            'oklch(0.6 0.22 22)',   // red
        NEUTRAL:        'oklch(0.5 0.03 258)',  // gray/blue
    };

    // 5-level Likert gradient, index 0 = level 1 (worst) .. index 4 = level 5 (best).
    const LIKERT_SCALE = [SEMANTIC.BAD, SEMANTIC.WARNING_STRONG, SEMANTIC.WARNING, SEMANTIC.GOOD_LIGHT, SEMANTIC.GOOD];

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

    function normalize(label) {
        return String(label ?? '').trim();
    }

    // ── Binary evaluation (Yes/No) questions ─────────────────────────────
    // e.g. "Would Return", "Fair Treatment", "Training Adequate".
    // Unrecognized text intentionally falls back to NEUTRAL rather than
    // guessing — a wrong guess (coloring a "No" green) is worse than gray.
    const YES_TOKENS = ['yes', 'y', 'true', 'نعم'];
    const NO_TOKENS  = ['no', 'n', 'false', 'لا'];

    function yesNoColor(label) {
        const s = normalize(label).toLowerCase();
        if (YES_TOKENS.includes(s)) return SEMANTIC.GOOD;
        if (NO_TOKENS.includes(s))  return SEMANTIC.BAD;
        return SEMANTIC.NEUTRAL;
    }

    function colorsByYesNo(labels) {
        return labels.map(yesNoColor);
    }

    // ── 5-point Likert-scale questions ───────────────────────────────────
    // e.g. "Overall Experience". Accepts a numeric 1-5 rating directly, or
    // normalizes common EN/AR phrasings (satisfaction, quality, and
    // agree/disagree wording) to the same 5 levels the caller's API might
    // use instead of numbers.
    //
    // Bucket check order is 3, 1, 2, 5, 4 (neutral, then negatives, then
    // positives) rather than 1..5 in order, because several phrases are
    // substrings of another level's phrase (e.g. "agree" is contained in
    // "disagree", "satisfied" in "dissatisfied", and the Arabic neutral
    // phrase "لا أوافق ولا أعارض" contains "أوافق"/"أعارض"). Checking the
    // more specific/negative phrases first avoids misreading them as the
    // shorter phrase they contain. This mirrors the substring-priority
    // approach already used by ExitInterviewService.Sentiment() server-side.
    const LIKERT_LEVEL_5 = ['excellent', 'very satisfied', 'strongly agree', 'ممتاز', 'أوافق بشدة'];
    const LIKERT_LEVEL_4 = ['good', 'satisfied', 'agree', 'جيد', 'جيدة', 'أوافق'];
    const LIKERT_LEVEL_3 = ['neutral', 'average', 'fair', 'ok', 'okay', 'محايد', 'مقبولة', 'لا أوافق ولا أعارض'];
    const LIKERT_LEVEL_2 = ['poor', 'dissatisfied', 'disagree', 'ضعيف', 'أعارض'];
    const LIKERT_LEVEL_1 = ['very poor', 'very dissatisfied', 'strongly disagree', 'bad', 'سيء', 'أعارض بشدة'];
    const LIKERT_BUCKETS = [
        [3, LIKERT_LEVEL_3],
        [1, LIKERT_LEVEL_1],
        [2, LIKERT_LEVEL_2],
        [5, LIKERT_LEVEL_5],
        [4, LIKERT_LEVEL_4],
    ];

    function likertLevel(label) {
        if (typeof label === 'number') {
            const n = Math.round(label);
            return n >= 1 && n <= 5 ? n : null;
        }
        const raw = normalize(label);
        if (raw === '') return null;
        const asNumber = Number(raw);
        if (!Number.isNaN(asNumber) && asNumber >= 1 && asNumber <= 5) return Math.round(asNumber);
        const lower = raw.toLowerCase();
        for (const [level, phrases] of LIKERT_BUCKETS) {
            if (phrases.some(p => lower.includes(p))) return level;
        }
        return null;
    }

    function likertColor(label) {
        const level = likertLevel(label);
        return level ? LIKERT_SCALE[level - 1] : SEMANTIC.NEUTRAL;
    }

    function colorsByLikert(labels) {
        return labels.map(likertColor);
    }

    return {
        SEMANTIC,
        LIKERT_SCALE,
        CATEGORICAL_PALETTE,
        GENDER_COLORS,
        RETENTION_MILESTONE_COLORS,
        RETENTION_TENURE_BUCKET_COLORS,
        EARLY_WARNING_REASON_COLORS,
        colorsByLabel,
        hashColor,
        yesNoColor,
        colorsByYesNo,
        likertLevel,
        likertColor,
        colorsByLikert,
    };
})();
