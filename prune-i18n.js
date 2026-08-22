// One-off maintenance: strip dead keys from pdf-landing/kp-i18n.js.
//
// WHY THIS EXISTS
// Site strings have been renamed across releases by suffixing (ph_224 -> ph_224_172,
// pt_55 -> pt_55_174). The new key was added everywhere; the old one was never removed. A few
// other keys belong to features that are simply gone - an older kb_* keyboard-label generation,
// f_look_*, f_filedlg_*. Result: roughly a third of a 1 MB file that no page ever asks for,
// shipped to every visitor.
//
// WHY IT IS A SCRIPT AND NOT AN EDIT
// The dead lines are scattered across ~185 non-contiguous runs in eleven locale blocks. That is
// not something to do by hand, and the project rules (rightly) keep shell writes away from
// tracked files. So this is a reviewable script you run once, and it refuses to write anything
// unless the result verifies.
//
// WHAT COUNTS AS LIVE
//   1. every data-i18n="..." key in index/help/technical/about.html
//   2. shot_01..shot_09, which index.html sets from JavaScript rather than data-i18n
// Those two lookups are the only ones in the codebase (kp.js:164 and index.html:298) and both
// use literal keys, so nothing is resolved dynamically and this list is complete.
//
// USAGE:  node prune-i18n.js          (report only, writes nothing)
//         node prune-i18n.js --apply  (rewrites kp-i18n.js after verifying)

const fs = require('fs');
const path = require('path');

const DIR = path.join(__dirname, 'pdf-landing');
const TARGET = path.join(DIR, 'kp-i18n.js');
const PAGES = ['index.html', 'help.html', 'technical.html', 'about.html'];
const apply = process.argv.includes('--apply');

// ---- live key set -------------------------------------------------------
const live = new Set();
for (const p of PAGES) {
    const file = path.join(DIR, p);
    if (!fs.existsSync(file)) continue;
    const html = fs.readFileSync(file, 'utf8');
    for (const m of html.matchAll(/data-i18n="([^"]+)"/g)) live.add(m[1]);
}
for (let i = 1; i <= 9; i++) live.add('shot_0' + i);
if (live.size === 0) { console.error('Found no data-i18n keys - wrong folder? Aborting.'); process.exit(1); }

// ---- before ------------------------------------------------------------
const original = fs.readFileSync(TARGET, 'utf8');
const before = load(original);
const locales = Object.keys(before);

// ---- strip -------------------------------------------------------------
// Line-based on purpose: it preserves formatting, comments and key order exactly, where
// re-serializing the object would reflow all 11 locale blocks and bury the real diff.
const LOCALE_HEAD = new RegExp('^\\s*"?(' + locales.join('|') + ')"?\\s*:\\s*\\{');
const removed = [];
const kept = original.split('\n').filter(line => {
    if (LOCALE_HEAD.test(line)) return true;              // never drop a locale opener
    const m = line.match(/^\s*"([^"]+)"\s*:/);
    if (!m || live.has(m[1])) return true;
    removed.push(m[1]);
    return false;
});
const output = kept.join('\n');

// ---- verify ------------------------------------------------------------
let after;
try { after = load(output); }
catch (e) { console.error('Result does not parse, refusing to write: ' + e.message); process.exit(1); }

const problems = [];
if (Object.keys(after).length !== locales.length)
    problems.push(`locale count changed: ${locales.length} -> ${Object.keys(after).length}`);

for (const l of locales) {
    for (const k of live) {
        // a key only has to survive if it was there to begin with
        if (before[l][k] !== undefined && after[l][k] === undefined)
            problems.push(`${l}: lost live key ${k}`);
        if (before[l][k] !== undefined && before[l][k] !== after[l][k])
            problems.push(`${l}: value changed for ${k}`);
    }
    for (const k of Object.keys(after[l])) {
        if (!live.has(k)) problems.push(`${l}: dead key ${k} survived`);
    }
}

console.log(`locales            : ${locales.length}`);
console.log(`live keys the site uses: ${live.size}`);
console.log(`dead entries removed   : ${removed.length}`);
console.log(`distinct dead keys     : ${new Set(removed).size}`);
console.log(`size                   : ${original.length} -> ${output.length} bytes ` +
            `(-${(100 - output.length / original.length * 100).toFixed(1)}%)`);

if (problems.length) {
    console.error('\nVERIFICATION FAILED, nothing written:');
    for (const p of problems.slice(0, 25)) console.error('  ' + p);
    process.exit(1);
}
console.log('\nverification passed: every live key intact, every dead key gone');

if (!apply) { console.log('\n(dry run - re-run with --apply to write)'); process.exit(0); }

fs.writeFileSync(TARGET + '.bak', original, 'utf8');
fs.writeFileSync(TARGET, output, 'utf8');
console.log('written. previous file saved as kp-i18n.js.bak');

// ---- helper ------------------------------------------------------------
function load(src) {
    const sandbox = {};
    new Function('g', src + '\n g.I18N = I18N;')(sandbox);
    return sandbox.I18N;
}
