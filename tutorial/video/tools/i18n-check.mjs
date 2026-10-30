// The texts the scenes draw that are missing from a language's table (src/lib/i18n.<lang>.ts).
//   node tools/i18n-check.mjs [--lang es] [--all]     --all lists every text, translated or not
// It looks for t('…') and tr('…') with a literal, and the labels of qi('…') / qt('…') (QuickMenu items).
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const args = process.argv.slice(2);
const lang = args.includes('--lang') ? args[args.indexOf('--lang') + 1] : 'es';
const files = [];
const walk = d => { for (const f of readdirSync(d)) { const p = join(d, f); statSync(p).isDirectory() ? walk(p) : /\.tsx?$/.test(f) && !/i18n/.test(f) && files.push(p); } };
walk(join(root, 'src'));

const texts = new Map(); // text → first file
const unescape = s => s.replace(/\\(.)/g, '$1');
for (const file of files) {
  const src = readFileSync(file, 'utf8');
  for (const m of src.matchAll(/\b(?:t|tr|qi|qt)\('((?:[^'\\]|\\.)*)'/g))
    if (!texts.has(unescape(m[1]))) texts.set(unescape(m[1]), file.slice(root.length + 1));
}

// The table: keys of the exported object (one 'English': 'translation' per line).
const table = readFileSync(join(root, 'src', 'lib', `i18n.${lang}.ts`), 'utf8');
const have = new Set([...table.matchAll(/^\s*('(?:[^'\\]|\\.)*'|"(?:[^"\\]|\\.)*")\s*:/gm)].map(m => unescape(m[1].slice(1, -1))));
const missing = [...texts.keys()].filter(k => !have.has(k));
const unused = [...have].filter(k => !texts.has(k));
for (const k of args.includes('--all') ? [...texts.keys()] : missing) console.log(`${JSON.stringify(k)}  (${texts.get(k)})`);
console.error(`${texts.size} texts, ${missing.length} missing in ${lang}${unused.length ? `, ${unused.length} unused: ${unused.map(u => JSON.stringify(u)).join(', ')}` : ''}`);
