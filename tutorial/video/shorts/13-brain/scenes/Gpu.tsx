import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Fluent } from '../../../src/components/Icon';
import { Sfx } from '../../../src/components/Sfx';
import { glide, keys, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';
import { blips, BRAIN } from './Model';

/** Chapter «Share the graphics card»: unloading when idle, and freeing the card for a full-screen game. */
export const Gpu: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const idle = B('idle');
  const g = B('games');
  const focus: Focus[] = [
    { from: idle.cue('unload') - 4, to: idle.cue('again') - 4, rect: BRAIN.unload },
    { from: idle.cue('again') - 4, to: g.from, rect: BRAIN.status, label: t('It loads by itself'), at: 'bottom' },
    { from: g.cue('game') - 4, to: g.to + 30, rect: BRAIN.games },
  ];
  const card = glide(f, g.cue('game') + 4, 22);
  const used = keys(f, [[g.cue('frees') - 4, 9.4], [g.cue('frees') + 22, 1.6]]);
  const film = pop(f, g.cue('film') - 6, { damping: 14 });
  return (
    <AbsoluteFill>
      <CaptureShot name="brain" focus={focus}
        shots={[
          { at: 0, x: 1050, y: 560, s: 1.25 },
          { at: 4, dur: 30, x: 1050, y: 560, s: 1.5 },
          { at: idle.cue('again') - 16, dur: 26, x: 1050, y: 300, s: 1.5 },
          { at: g.cue('game') - 16, dur: 26, x: 1050, y: 560, s: 1.5 },
        ]} />
      <div style={{
        position: 'absolute', left: 960, top: 900, width: 880, padding: '22px 28px', borderRadius: 22, boxSizing: 'border-box', fontFamily: SANS,
        transform: `translate(-50%, -50%) translateY(${(1 - card) * 40}px)`, opacity: card,
        background: 'rgba(28,28,34,0.97)', border: `1.5px solid ${C.line2}`, boxShadow: '0 20px 60px rgba(0,0,0,0.6)',
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 14, marginBottom: 14 }}>
          <Fluent name="games_24_filled" size={30} color={C.success} />
          <span style={{ fontSize: 24, fontWeight: 700, color: C.text }}>{t('Graphics card')}</span>
          <span style={{ marginLeft: 'auto', fontSize: 22, color: C.dim }}>{used.toFixed(1)} / 11.7 GB</span>
        </div>
        <div style={{ height: 14, borderRadius: 7, background: 'rgba(255,255,255,0.1)', overflow: 'hidden' }}>
          <div style={{ width: `${(used / 11.7) * 100}%`, height: '100%', borderRadius: 7, background: `linear-gradient(90deg, ${C.success}, ${C.accentText})` }} />
        </div>
      </div>
      <div style={{
        position: 'absolute', left: 1440, top: 770, transform: `translate(-50%, -50%) scale(${film})`, opacity: Math.min(1, film * 1.4), fontFamily: SANS,
        display: 'flex', alignItems: 'center', gap: 12, padding: '12px 22px', borderRadius: 30, background: 'rgba(28,28,34,0.97)', border: `1.5px solid ${C.line2}`, fontSize: 24, fontWeight: 600, color: C.text,
      }}>
        <Fluent name="video_clip_24_filled" size={28} color={C.sky} /> {t('A film doesn’t count')}
      </div>
      {blips(focus)}
      <Sfx at={g.cue('frees')} name="whoosh-down" volume={0.22} />
      <Sfx at={g.cue('film') - 6} name="pop" volume={0.18} />
    </AbsoluteFill>
  );
};
