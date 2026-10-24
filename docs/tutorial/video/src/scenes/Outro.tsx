import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { KdIcon } from '../components/Icon';
import { Mono } from '../components/Keys';
import { Sfx } from '../components/Sfx';
import { ease, glide, IN, OUT, pop, rand } from '../lib/anim';
import { useBeats, useChapter } from '../lib/timeline';
import { C, SANS } from '../theme';

const TILES = [
  { cue: 'commands', icon: 'flash_24_filled', color: 'gold' }, { cue: 'commands', icon: 'window_new_24_filled', color: 'sky' },
  { cue: 'qm', icon: 'text_bullet_list_tree_24_filled', color: 'purple' }, { cue: 'qm', icon: 'search_24_filled', color: 'yellow' },
  { cue: 'events', icon: 'arrow_forward_down_lightning_24_filled', color: 'orange' }, { cue: 'events', icon: 'record_24_filled', color: 'red' }, { cue: 'events', icon: 'keyboard_layout_float_24_filled', color: 'indigo' },
  { cue: 'voice', icon: 'mic_24_filled', color: 'teal' }, { cue: 'phone', icon: 'phone_24_filled', color: 'sky' }, { cue: 'assistants', icon: 'bot_24_filled', color: 'pink' },
];

/** Recap, where to get it, thanks. */
export const Outro: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const recap = B('recap');
  const cta = B('cta');
  const end = B('end');
  const gather = ease(f, cta.from - 20, cta.from + 10, IN);
  const logo = pop(f, cta.from + 4, { damping: 12, stiffness: 110 });
  const fade = ease(f, durationInFrames - 50, durationInFrames - 5);
  return (
    <AbsoluteFill>
      {TILES.map((t, i) => {
        const k = pop(f, recap.cue(t.cue) - 6 + (i % 3) * 3, { damping: 12 });
        const angle = (i / TILES.length) * Math.PI * 2 + f / 200;
        const x = 960 + Math.cos(angle) * 520 * (1 - gather);
        const y = 440 + Math.sin(angle) * 280 * (1 - gather);
        return <div key={i} style={{ position: 'absolute', left: x - 70, top: y - 70, transform: `scale(${k * (1 - gather)}) rotate(${(rand(i) - 0.5) * 20}deg)` }}><KdIcon icon={t.icon} color={t.color} size={140} glow={0.4} /></div>;
      })}
      <div style={{ position: 'absolute', left: 960, top: 300, transform: `translate(-50%, -50%) scale(${logo})` }}>
        <Img src={staticFile('brand/icon-512.png')} style={{ width: 240, height: 240, filter: 'drop-shadow(0 20px 60px rgba(224,168,21,0.55))' }} />
      </div>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 450, textAlign: 'center', fontFamily: SANS, fontWeight: 800, fontSize: 100, letterSpacing: -2, color: C.text, opacity: ease(f, cta.from + 10, cta.from + 30, OUT) }}>
        K-Desktop <span style={{ color: C.accentText }}>5</span>
      </div>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 600, display: 'flex', justifyContent: 'center', gap: 18, opacity: ease(f, cta.cue('single'), cta.cue('single') + 16) }}>
        {['KDesktop.exe', 'portable', 'Windows 10 · 11', '6 languages'].map(t => <span key={t} style={{ fontFamily: SANS, fontSize: 26, fontWeight: 700, padding: '10px 20px', borderRadius: 24, background: 'rgba(255,255,255,0.07)', color: C.text }}>{t}</span>)}
      </div>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 700, display: 'flex', justifyContent: 'center', opacity: glide(f, cta.cue('download') - 6, 20) }}>
        <div style={{ padding: '16px 34px', borderRadius: 18, background: 'linear-gradient(180deg,#FFD970,#F2B807)', boxShadow: '0 20px 50px rgba(242,193,74,0.35)' }}>
          <Mono size={34} color="#1a1306">github.com/Kobayashi82/K-Desktop</Mono>
        </div>
      </div>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 810, textAlign: 'center', fontFamily: SANS, fontSize: 34, fontWeight: 700, color: C.dim, opacity: ease(f, end.from - 6, end.from + 10) }}>Thanks for watching</div>
      <AbsoluteFill style={{ background: `rgba(0,0,0,${fade})` }} />
      {TILES.map((t, i) => <Sfx key={i} at={recap.cue(t.cue) - 6 + (i % 3) * 3} name="pop-high" volume={0.1} />)}
      <Sfx at={cta.from - 24} name="rise" volume={0.35} />
      <Sfx at={cta.from + 4} name="impact" volume={0.4} />
      <Sfx at={cta.cue('download') - 6} name="chime" volume={0.25} />
    </AbsoluteFill>
  );
};
