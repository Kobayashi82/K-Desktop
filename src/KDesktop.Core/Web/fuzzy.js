// Same matching as the desktop launcher (KDesktop.Core/Engine/FuzzyMatch.cs): every word of the query must match;
// whole text > start > start of a word > initials > inside a word > letters in order. No accents, no case.

const isAlnum = c => /[\p{L}\p{N}]/u.test(c);
const isUpper = c => c !== c.toLowerCase() && c === c.toUpperCase();
const isLower = c => c !== c.toUpperCase() && c === c.toLowerCase();
const isDigit = c => c >= '0' && c <= '9';

export function fold(c) {
  const d = c.normalize('NFD');
  return (/\p{Mn}/u.test(d[0]) ? c : d[0]).toLowerCase();
}

export function searchKey(text) {
  const chars = [...text];
  const folded = [];
  const starts = [];
  let initials = '';
  chars.forEach((c, i) => {
    const f = fold(c);
    folded.push(f.length === 1 ? f : c);
    const prev = i > 0 ? chars[i - 1] : ' ';
    const start = isAlnum(c) && (!isAlnum(prev) || (isUpper(c) && isLower(prev)) || (isDigit(c) !== isDigit(prev)));
    starts.push(start);
    if (start) initials += folded[i];
  });
  return { text, folded: folded.join(''), starts, initials };
}

export function tokens(query) {
  return [...query].map(fold).join('').split(/\s+/).filter(Boolean);
}

export function score(toks, key) {
  if (!toks.length) return 0;
  let total = 0;
  for (const t of toks) {
    const s = tokenScore(t, key);
    if (!s) return 0;
    total += s;
  }
  let result = Math.floor(total / toks.length);
  if (toks.length > 1 && key.folded.includes(toks.join(' '))) result += 50;
  return result;
}

function tokenScore(token, key) {
  const text = key.folded;
  if (!text.length) return 0;
  if (text === token) return 1000;
  if (text.startsWith(token)) return 900 + Math.floor(50 * token.length / text.length);
  for (let i = 1; i <= text.length - token.length; i++)
    if (key.starts[i] && text.startsWith(token, i)) return 800 - Math.min(i, 50);
  if (token.length >= 2 && key.initials.startsWith(token)) return 720;
  const inside = text.indexOf(token);
  if (inside >= 0) return token.length >= 2 ? 600 - Math.min(inside, 100) : 0;
  return token.length >= 3 ? subsequence(token, key) : 0;
}

function subsequence(token, key) {
  const text = key.folded;
  let best = 0;
  for (let start = 0; start < text.length; start++) {
    if (!key.starts[start] || text[start] !== token[0]) continue;
    let last = start, gaps = 0, gapChars = 0, matched = 1;
    for (let pos = start + 1; matched < token.length && pos < text.length; pos++) {
      if (text[pos] !== token[matched]) continue;
      if (pos > last + 1) { gaps++; gapChars += pos - last - 1; }
      last = pos;
      matched++;
    }
    if (matched < token.length || gapChars > token.length * 3) continue;
    best = Math.max(best, 450 - gaps * 40 - gapChars * 5 - Math.min(start, 30));
  }
  return Math.max(best, 0);
}
