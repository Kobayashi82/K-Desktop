import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { KdIcon } from '../../../src/components/Icon';
import { Sfx } from '../../../src/components/Sfx';
import { inOut, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { BeatTimes } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';

const WAYS = [
  { cue: 'menu', icon: 'text_bullet_list_tree_24_filled', color: 'purple', title: t('QuickMenu'), sub: t('Under the mouse') },
  { cue: 'launcher', icon: 'search_24_filled', color: 'yellow', title: t('Launcher'), sub: t('A few letters') },
  { cue: 'voice', icon: 'mic_24_filled', color: 'teal', title: t('Voice'), sub: t('“Work mode”') },
  { cue: 'phone', icon: 'phone_24_filled', color: 'sky', title: t('Your phone'), sub: t('Web app · Android') },
  { cue: 'network', icon: 'plug_connected_24_filled', color: 'pink', title: t('Another computer'), sub: t('Scripts · Home Assistant') },
  { cue: 'event', icon: 'arrow_forward_down_lightning_24_filled', color: 'orange', title: t('An event'), sub: t('When something happens') },
  { cue: 'assistant', icon: 'bot_24_filled', color: 'purple', title: t('An assistant'), sub: t('Just ask') },
];

/** Every way to run a command without a hotkey, around the command itself. */
export const AnywherePart: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const center = pop(f, b.from - 4, { damping: 14 });
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <div style={{ position: 'absolute', left: 960, top: 200, transform: `translate(-50%, -50%) scale(${center})`, display: 'flex', alignItems: 'center', gap: 18, padding: '18px 30px', borderRadius: 24, background: 'rgba(30,30,36,0.95)', border: '1.5px solid rgba(242,193,74,0.5)' }}>
        <KdIcon icon="flash_24_filled" color="gold" size={64} />
        <span style={{ fontSize: 40, fontWeight: 800, color: C.text }}>{t('Work mode')}</span>
      </div>
      <div style={{ position: 'absolute', left: 100, right: 100, top: 360, display: 'flex', flexWrap: 'wrap', justifyContent: 'center', gap: 26 }}>
        {WAYS.map(w => {
          const k = pop(f, b.cue(w.cue) - 6, { damping: 14, stiffness: 150 });
          const lit = inOut(f, b.cue(w.cue) - 4, b.cue(w.cue) + 30, 6);
          return (
            <div key={w.cue} style={{
              width: 420, display: 'flex', alignItems: 'center', gap: 18, padding: '20px 24px', borderRadius: 22, boxSizing: 'border-box',
              background: 'rgba(30,30,36,0.94)', border: `1.5px solid rgba(242,193,74,${0.12 + 0.6 * lit})`, boxShadow: `0 20px 50px rgba(0,0,0,0.45), 0 0 ${40 * lit}px rgba(242,193,74,${0.2 * lit})`,
              opacity: Math.min(1, k * 1.4), transform: `translateY(${(1 - k) * 50}px) scale(${0.85 + 0.15 * k})`,
            }}>
              <KdIcon icon={w.icon} color={w.color} size={64} />
              <div>
                <div style={{ fontSize: 29, fontWeight: 700, color: C.text, whiteSpace: 'nowrap' }}>{w.title}</div>
                <div style={{ fontSize: 20, color: C.dim, marginTop: 2 }}>{w.sub}</div>
              </div>
            </div>
          );
        })}
      </div>
      <Sfx at={b.from - 4} name="pop" volume={0.2} />
      {WAYS.map(w => <Sfx key={w.cue} at={b.cue(w.cue) - 6} name="pop-high" volume={0.15} />)}
    </AbsoluteFill>
  );
};
