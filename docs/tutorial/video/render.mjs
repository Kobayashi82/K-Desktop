// Renders the whole video: node render.mjs [--lang en] [--scale 1] [--crf 18] [--chapter <id>] [--concurrency <n>]
// The subtitles go beside it as a .srt with the same name (players pick it up), never burnt into the picture.
// Needs the narration (python tools/tts.py --lang <lang>) and the assets (python tools/assets.py, node tools/webapp.mjs).
import { bundle } from '@remotion/bundler';
import { renderMedia, selectComposition } from '@remotion/renderer';
import { spawnSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync, renameSync, unlinkSync } from 'node:fs';
import { availableParallelism } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { browserExecutable, webpackOverride } from './tools/common.mjs';

const root = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const option = (name, fallback) => (args.includes(`--${name}`) ? args[args.indexOf(`--${name}`) + 1] : fallback);
const lang = option('lang', 'en');
const scale = Number(option('scale', '1'));
const crf = Number(option('crf', '18'));
const chapter = option('chapter', null);
const output = join(root, 'out', `K-Desktop-${lang}${chapter ? `-${chapter}` : ''}${scale !== 1 ? `-${Math.round(1080 * scale)}p` : ''}.mp4`);

const serveUrl = await bundle({ entryPoint: join(root, 'src', 'index.ts'), webpackOverride });
const inputProps = { lang, music: true, ...(chapter ? { chapter } : {}) };
const composition = await selectComposition({ serveUrl, id: chapter ? 'chapter' : `kdesktop-${lang}`, inputProps, browserExecutable });
mkdirSync(join(root, 'out'), { recursive: true });
let last = -1;
await renderMedia({
  serveUrl, composition, inputProps, browserExecutable, codec: 'h264', crf, scale, outputLocation: output,
  audioBitrate: '192k', pixelFormat: 'yuv420p', concurrency: Number(option('concurrency', String(availableParallelism()))), timeoutInMilliseconds: 120000,
  onProgress: ({ progress }) => {
    const pct = Math.floor(progress * 100);
    if (pct !== last && pct % 2 === 0) console.log(`${pct}%`);
    last = pct;
  },
});

// Mastering: the whole mix to -15 LUFS (true peak -1.5 dB) with two passes of ffmpeg's loudnorm; the picture is copied.
const NORM = 'loudnorm=I=-15:TP=-1.5:LRA=11';
const first = spawnSync('ffmpeg', ['-hide_banner', '-i', output, '-af', `${NORM}:print_format=json`, '-f', 'null', '-'], { encoding: 'utf8' });
const m = JSON.parse(first.stderr.slice(first.stderr.lastIndexOf('{')));
const raw = output.replace(/\.mp4$/, '.raw.mp4');
renameSync(output, raw);
const second = spawnSync('ffmpeg', ['-hide_banner', '-y', '-i', raw, '-c:v', 'copy', '-af',
  `${NORM}:measured_I=${m.input_i}:measured_TP=${m.input_tp}:measured_LRA=${m.input_lra}:measured_thresh=${m.input_thresh}:offset=${m.target_offset}:linear=true`,
  '-ar', '48000', '-c:a', 'aac', '-b:a', '192k', '-movflags', '+faststart', output], { encoding: 'utf8' });
if (second.status !== 0) throw new Error(second.stderr);
unlinkSync(raw);
console.log(`${output} (audio ${m.input_i} → -15 LUFS)`);

// The subtitles of the whole video (tools/tts.py), beside it.
const srt = join(root, 'out', `subtitles.${lang}.srt`);
if (!chapter && existsSync(srt)) {
  copyFileSync(srt, output.replace(/\.mp4$/, '.srt'));
  console.log(output.replace(/\.mp4$/, '.srt'));
}
