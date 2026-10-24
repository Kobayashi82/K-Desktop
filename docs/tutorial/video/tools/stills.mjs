// Renders some frames of a chapter as PNG, to look at them without rendering the video.
//   node tools/stills.mjs <chapter> <frame> [<frame>…] [--lang en] [--scale 0.5] [--subtitles]
//   node tools/stills.mjs all --every 180 [--scale 0.25]     one frame every 6 seconds of the whole video
// Frames are relative to the chapter («all»: to the video); «50%» is the middle. They land in out/stills/.
import { bundle } from '@remotion/bundler';
import { openBrowser, renderStill, selectComposition } from '@remotion/renderer';
import { mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { browserExecutable, webpackOverride } from './common.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const args = process.argv.slice(2);
const option = (name, fallback) => {
  const i = args.indexOf(`--${name}`);
  if (i < 0) return fallback;
  const [value] = args.splice(i, 2).slice(1);
  return value;
};
const subtitles = args.includes('--subtitles') && !!args.splice(args.indexOf('--subtitles'), 1);
const lang = option('lang', 'en');
const scale = Number(option('scale', '0.5'));
const every = Number(option('every', '0'));
const [chapter, ...frames] = args;
const whole = chapter === 'all';

const serveUrl = await bundle({ entryPoint: join(root, 'src', 'index.ts'), webpackOverride });
const inputProps = { lang, music: false, subtitles, ...(whole ? {} : { chapter }) };
const composition = await selectComposition({ serveUrl, id: whole ? `kdesktop-${lang}` : 'chapter', inputProps, browserExecutable });
const list = every
  ? Array.from({ length: Math.ceil(composition.durationInFrames / every) }, (_, i) => i * every)
  : frames.map(f => f.endsWith('%') ? Math.round((composition.durationInFrames - 1) * Number(f.slice(0, -1)) / 100) : Number(f));
mkdirSync(join(root, 'out', 'stills'), { recursive: true });
const puppeteerInstance = await openBrowser('chrome', { browserExecutable });
for (const frame of list) {
  const output = join(root, 'out', 'stills', `${chapter}-${String(frame).padStart(5, '0')}.png`);
  await renderStill({ serveUrl, composition, frame, output, scale, inputProps, browserExecutable, puppeteerInstance });
  console.log(output);
}
await puppeteerInstance.close({ silent: true });
