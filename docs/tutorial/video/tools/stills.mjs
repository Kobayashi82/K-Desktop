// Renders some frames of a chapter as PNG, to look at them without rendering the video.
//   node tools/stills.mjs <chapter> <frame> [<frame>…] [--lang en] [--scale 0.5]
// Frames are relative to the chapter; «50%» is the middle. They land in out/stills/.
import { bundle } from '@remotion/bundler';
import { renderStill, selectComposition } from '@remotion/renderer';
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
const lang = option('lang', 'en');
const scale = Number(option('scale', '0.5'));
const [chapter, ...frames] = args;

const serveUrl = await bundle({ entryPoint: join(root, 'src', 'index.ts'), webpackOverride });
const composition = await selectComposition({ serveUrl, id: 'chapter', inputProps: { lang, chapter }, browserExecutable });
mkdirSync(join(root, 'out', 'stills'), { recursive: true });
for (const f of frames) {
  const frame = f.endsWith('%') ? Math.round((composition.durationInFrames - 1) * Number(f.slice(0, -1)) / 100) : Number(f);
  const output = join(root, 'out', 'stills', `${chapter}-${String(frame).padStart(5, '0')}.png`);
  await renderStill({ serveUrl, composition, frame, output, scale, inputProps: { lang, chapter, music: false }, browserExecutable });
  console.log(output);
}
