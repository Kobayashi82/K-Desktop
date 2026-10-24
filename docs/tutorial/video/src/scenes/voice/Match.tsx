import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Kinetic } from '../../components/Diagram';
import { Fluent, KdIcon } from '../../components/Icon';
import { Mono } from '../../components/Keys';
import { Sfx } from '../../components/Sfx';
import { ease, OUT, pop, rand, SMOOTH } from '../../lib/anim';
import { BeatTimes } from '../../lib/timeline';
import { C, SANS } from '../../theme';

const Token: React.FC<{ text: string; tone?: 'normal' | 'filler' | 'verb' | 'gold'; show?: number; note?: string }> = ({ text, tone = 'normal', show = 1, note }) => (
  <div style={{ position: 'relative', opacity: show }}>
    <div style={{
      padding: '16px 26px', borderRadius: 18, fontFamily: SANS, fontSize: 46, fontWeight: 800, whiteSpace: 'nowrap',
      background: tone === 'gold' ? C.accentText : tone === 'verb' ? 'rgba(85,210,255,0.16)' : 'rgba(34,34,40,0.96)',
      color: tone === 'gold' ? '#1a1306' : tone === 'filler' ? 'rgba(255,255,255,0.25)' : C.text,
      border: `2px solid ${tone === 'verb' ? 'rgba(85,210,255,0.6)' : tone === 'gold' ? C.gold1 : C.line2}`,
      textDecoration: tone === 'filler' ? 'line-through' : 'none', boxShadow: tone === 'gold' ? '0 0 40px rgba(242,193,74,0.5)' : 'none',
    }}>{text}</div>
    {note && <div style={{ position: 'absolute', left: 0, right: 0, top: 96, textAlign: 'center', fontFamily: SANS, fontSize: 20, fontWeight: 700, color: C.accentText, whiteSpace: 'nowrap' }}>{note}</div>}
  </div>
);

/** First layer: the words. */
export const WordsPart: React.FC<{ match: BeatTimes; words: BeatTimes }> = ({ match, words }) => {
  const f = useCurrentFrame();
  const filler = f > words.cue('filler') - 4;
  const merged = f > words.cue('youtube') + 6;
  const scanning = f > words.cue('name') - 6;
  const commands = [
    { name: 'YouTube', icon: 'video_clip_24_filled', color: 'red', score: merged ? 0.97 : 0.58 },
    { name: 'Music', icon: 'music_note_2_24_filled', color: 'pink', score: 0.12 },
    { name: 'Browser', icon: 'globe_24_filled', color: 'blue', score: 0.21 },
    { name: 'Downloads', icon: 'arrow_download_24_filled', color: 'teal', score: 0.08 },
  ];
  const scoreK = ease(f, words.cue('name'), words.cue('name') + 30);
  return (
    <AbsoluteFill>
      <Kinetic text="Which command did you mean?" at={match.from} x={960} y={170} size={64} color={C.dim} weight={700} />
      <div style={{ position: 'absolute', left: 0, right: 0, top: 330, display: 'flex', justifyContent: 'center', gap: 22, alignItems: 'center' }}>
        <div style={{ fontFamily: SANS, fontSize: 40, color: C.faint, marginRight: 10, opacity: ease(f, match.cue('finding') - 10, match.cue('finding')) }}>
          <Fluent name="mic_24_filled" size={50} color={C.teal} />
        </div>
        {[['please', 'filler'], ['open', 'verb'], ['the', 'filler']].map(([t, tone], i) => (
          <Token key={t} text={t} tone={filler ? (tone as 'filler' | 'verb') : 'normal'} show={pop(f, match.cue('finding') - 6 + i * 4)}
            note={t === 'open' && f > words.cue('endings') - 4 && f < words.cue('youtube') ? 'open · opens · opening' : undefined} />
        ))}
        {merged ? (
          <Token text="YouTube" tone="gold" show={pop(f, words.cue('youtube') + 6)} note="sounds like · joined" />
        ) : (
          ['you', 'tube'].map((t, i) => (
            <div key={t} style={{ transform: `translateX(${(i ? -1 : 1) * 30 * ease(f, words.cue('youtube') - 10, words.cue('youtube') + 6, SMOOTH)}px)` }}>
              <Token text={t} show={pop(f, match.cue('finding') + 6 + i * 4)}
                note={i === 0 && f > words.cue('slips') - 4 ? (f > words.cue('sound') - 4 ? '≈ youtube?' : 'a slip?') : undefined} />
            </div>
          ))
        )}
      </div>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 620, display: 'flex', justifyContent: 'center', gap: 30, opacity: ease(f, words.cue('name') - 10, words.cue('name') + 4) }}>
        {commands.map((c, i) => {
          const win = merged && i === 0;
          return (
            <div key={c.name} style={{
              width: 330, padding: 24, borderRadius: 22, background: win ? 'rgba(242,193,74,0.14)' : 'rgba(34,34,40,0.96)', border: `2px solid ${win ? C.accentText : C.line2}`,
              boxShadow: win ? '0 0 50px rgba(242,193,74,0.35)' : '0 20px 50px rgba(0,0,0,0.4)', transform: `scale(${win ? 1.06 : 1})`,
            }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 14, marginBottom: 18 }}>
                <KdIcon icon={c.icon} color={c.color} size={50} />
                <span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 800, color: C.text }}>{c.name}</span>
              </div>
              <div style={{ height: 12, borderRadius: 6, background: 'rgba(255,255,255,0.08)' }}>
                <div style={{ width: `${(scanning ? c.score : 0) * 100 * scoreK}%`, height: '100%', borderRadius: 6, background: win ? C.accentText : C.teal }} />
              </div>
              <div style={{ marginTop: 10, textAlign: 'right' }}><Mono size={20} color={win ? C.accentText : C.dim}>{(c.score * scoreK).toFixed(2)}</Mono></div>
            </div>
          );
        })}
      </div>
      <Sfx at={match.cue('finding') - 6} name="pop" volume={0.2} />
      <Sfx at={words.cue('filler') - 4} name="whoosh-short" volume={0.2} />
      <Sfx at={words.cue('youtube') + 6} name="chime" volume={0.25} />
    </AbsoluteFill>
  );
};

type Pt = { label: string; x: number; y: number; kind: 'cmd' | 'none' | 'power' };
const POINTS: Pt[] = [
  { label: 'Empty Recycle Bin', x: 470, y: 640, kind: 'cmd' },
  { label: 'Downloads', x: 640, y: 420, kind: 'cmd' },
  { label: 'Screenshot', x: 930, y: 290, kind: 'cmd' },
  { label: 'Open browser', x: 1230, y: 340, kind: 'cmd' },
  { label: 'Music', x: 1420, y: 520, kind: 'cmd' },
  { label: 'Work mode', x: 820, y: 560, kind: 'cmd' },
  { label: 'Shut down the computer', x: 1060, y: 770, kind: 'power' },
  { label: 'turn on the lights', x: 1610, y: 820, kind: 'none' },
  { label: 'set an alarm', x: 1700, y: 720, kind: 'none' },
  { label: 'turn off the radio', x: 1500, y: 960, kind: 'none' },
  { label: "what's the weather", x: 1730, y: 960, kind: 'none' },
];

/** Second layer: the meaning, as points in a space. */
export const SpacePart: React.FC<{ meaning: BeatTimes; meaning2: BeatTimes; notmine: BeatTimes }> = ({ meaning, meaning2, notmine }) => {
  const f = useCurrentFrame();
  const vectors = meaning.cue('vectors');
  const space = meaning.cue('space');
  const cleanAt = meaning2.cue('clean');
  const emptyAt = meaning2.cue('empty');
  const tvAt = notmine.cue('tv');
  const fly = ease(f, cleanAt, cleanAt + 30, SMOOTH);
  const sentenceX = 960 + (560 - 960) * fly;
  const sentenceY = 230 + (575 - 230) * fly;
  const tvShow = pop(f, tvAt - 4);
  const nothing = pop(f, notmine.cue('nothing') - 4, { damping: 10 });
  const ring = ease(f, notmine.cue('powers') - 4, notmine.cue('powers') + 14, OUT);
  const tv = { x: 1230, y: 900 };
  const shut = POINTS[6];
  const radio = POINTS[9];
  const line = (a: { x: number; y: number }, b: { x: number; y: number }, show: number, color: string, label: string, dashed = false) => show > 0 && (
    <>
      <svg style={{ position: 'absolute', left: 0, top: 0, overflow: 'visible' }} width={1} height={1}>
        <line x1={a.x} y1={a.y} x2={a.x + (b.x - a.x) * show} y2={a.y + (b.y - a.y) * show} stroke={color} strokeWidth={4} strokeDasharray={dashed ? '10 10' : undefined} />
      </svg>
      <div style={{ position: 'absolute', left: (a.x + b.x) / 2, top: (a.y + b.y) / 2 - 34, transform: 'translateX(-50%)', opacity: show, fontFamily: SANS, fontSize: 24, fontWeight: 800, color, background: 'rgba(11,11,14,0.85)', padding: '2px 10px', borderRadius: 8 }}>{label}</div>
    </>
  );
  return (
    // Drawn a little smaller and from the top, so the bottom of the plane stays clear of the subtitles.
    <AbsoluteFill style={{ transform: 'scale(0.84)', transformOrigin: '960px 120px' }}>
      {/* The plane */}
      <div style={{ position: 'absolute', left: 140, top: 160, width: 1720, height: 870, borderRadius: 40, border: `1px solid ${C.line2}`, background: 'radial-gradient(ellipse at 50% 50%, rgba(62,220,193,0.06), transparent 70%)', opacity: ease(f, space - 20, space), backgroundImage: 'linear-gradient(rgba(255,255,255,0.04) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.04) 1px, transparent 1px)', backgroundSize: '86px 87px' }} />
      <div style={{ position: 'absolute', left: 180, top: 190, display: 'flex', alignItems: 'center', gap: 14, opacity: ease(f, meaning.cue('gemma') - 6, meaning.cue('gemma') + 6) }}>
        <KdIcon icon="brain_circuit_24_filled" color="teal" size={56} />
        <div>
          <div style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.text }}>EmbeddingGemma</div>
          <div style={{ fontFamily: SANS, fontSize: 19, color: C.dim }}>Google · 300M · int8 · on your PC</div>
        </div>
      </div>
      {POINTS.map((p, i) => {
        const k = pop(f, space - 4 + i * 3, { damping: 12 });
        const isNone = p.kind === 'none';
        const lit = (p.label === 'Empty Recycle Bin' && f > emptyAt - 4 && f < tvAt) || (isNone && f > notmine.cue('everyday') - 4);
        const colour = isNone ? '#8a8f99' : p.kind === 'power' ? C.danger : C.accentText;
        return (
          <div key={p.label} style={{ position: 'absolute', left: p.x, top: p.y, transform: `translate(-50%, -50%) scale(${k})`, opacity: k, display: 'flex', alignItems: 'center', gap: 12 }}>
            <div style={{ width: lit ? 30 : 22, height: lit ? 30 : 22, borderRadius: 15, background: colour, boxShadow: `0 0 ${lit ? 30 : 12}px ${colour}` }} />
            <span style={{ fontFamily: SANS, fontSize: isNone ? 21 : 25, fontWeight: isNone ? 500 : 700, color: isNone ? '#9aa0aa' : C.text, fontStyle: isNone ? 'italic' : 'normal', whiteSpace: 'nowrap' }}>{p.label}</span>
          </div>
        );
      })}
      {/* «Not mine» cloud */}
      <div style={{ position: 'absolute', left: 1450, top: 670, width: 420, height: 340, borderRadius: 170, border: '2px dashed rgba(160,165,175,0.5)', opacity: ease(f, notmine.cue('everyday') - 6, notmine.cue('everyday') + 6) }}>
        <div style={{ position: 'absolute', left: 0, right: 0, top: -40, textAlign: 'center', fontFamily: SANS, fontSize: 22, fontWeight: 700, color: '#aab' }}>Not K-Desktop's</div>
      </div>
      {/* The sentence and its vector */}
      <div style={{ position: 'absolute', left: sentenceX, top: sentenceY, transform: `translate(-50%, -50%) scale(${1 - 0.35 * fly})`, opacity: pop(f, meaning.cue('vectors') - 30) * (1 - ease(f, tvAt - 10, tvAt)), display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 16 }}>
        <div style={{ padding: '18px 30px', borderRadius: 22, background: 'white', fontFamily: SANS, fontSize: 40, fontWeight: 800, color: '#141418', whiteSpace: 'nowrap' }}>“clean out the trash”</div>
        <div style={{ opacity: ease(f, vectors, vectors + 12) * (1 - fly), display: 'flex', gap: 8 }}>
          {Array.from({ length: 9 }, (_, i) => <Mono key={i} size={22} color={C.teal}>{i === 8 ? '… 768' : (rand(i + 4) * 2 - 1).toFixed(2)}</Mono>)}
        </div>
      </div>
      {line({ x: 560, y: 600 }, POINTS[0], ease(f, emptyAt - 6, emptyAt + 10) * (1 - ease(f, tvAt - 10, tvAt)), C.success, '0.86')}
      {/* «Turn off the TV» */}
      <div style={{ position: 'absolute', left: tv.x, top: tv.y, transform: `translate(-50%, -50%) scale(${tvShow})`, opacity: tvShow, display: 'flex', alignItems: 'center', gap: 12 }}>
        <div style={{ width: 26, height: 26, borderRadius: 13, background: 'white', boxShadow: '0 0 20px white' }} />
        <span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 800, color: 'white', whiteSpace: 'nowrap' }}>“turn off the TV”</span>
      </div>
      {line(tv, shut, ease(f, notmine.cue('shutdown') - 4, notmine.cue('shutdown') + 12), C.danger, '0.79', true)}
      {line(tv, radio, ease(f, notmine.cue('everyday'), notmine.cue('everyday') + 16), '#c9ccd3', '0.88 — closer')}
      <div style={{ position: 'absolute', left: shut.x, top: shut.y, width: 200, height: 200, marginLeft: -100, marginTop: -100, borderRadius: 100, border: `4px solid ${C.danger}`, opacity: ring * 0.9, transform: `scale(${0.4 + 0.6 * ring})` }} />
      <div style={{ position: 'absolute', left: shut.x - 230, top: shut.y + 70, fontFamily: SANS, fontSize: 24, fontWeight: 800, color: C.danger, opacity: ring }}>Power commands need 0.90+</div>
      <div style={{
        position: 'absolute', left: 960, top: 470, transform: `translate(-50%, -50%) rotate(-8deg) scale(${nothing})`, opacity: nothing * (1 - ease(f, notmine.cue('powers') - 10, notmine.cue('powers'))),
        padding: '18px 40px', borderRadius: 20, border: `6px solid ${C.text}`, fontFamily: SANS, fontSize: 64, fontWeight: 900, color: C.text, letterSpacing: 2, background: 'rgba(11,11,14,0.6)',
      }}>NOTHING HAPPENS</div>
      {POINTS.map((p, i) => <Sfx key={p.label} at={space - 4 + i * 3} name="blip" volume={0.08} />)}
      <Sfx at={cleanAt} name="whoosh-short" volume={0.25} />
      <Sfx at={emptyAt - 4} name="chime" volume={0.22} />
      <Sfx at={tvAt - 4} name="pop" volume={0.25} />
      <Sfx at={notmine.cue('nothing') - 4} name="impact" volume={0.25} />
    </AbsoluteFill>
  );
};
