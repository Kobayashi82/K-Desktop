import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { KdIcon } from '../../../src/components/Icon';
import { ShortAgenda, ShortTitle } from '../../../src/components/Short';
import { Sfx } from '../../../src/components/Sfx';
import { ease, IN, pop, tween } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';

const CHORES = [
  { label: 'Open the same programs', icon: 'open_24_filled', color: 'blue' },
  { label: 'Move the same windows', icon: 'window_new_24_filled', color: 'sky' },
  { label: 'Type the same text', icon: 'keyboard_24_filled', color: 'purple' },
];

/** Three chores that keep coming back (a counter ticks up on each), swept away by the title and what the video covers. */
export const Hook: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const repeat = B('repeat');
  const plan = B('plan');
  const kd = repeat.cue('kd');
  const sweep = ease(f, kd - 14, kd + 6, IN);
  const ways = [
    { cue: 'key', label: t('a key'), icon: 'keyboard_24_filled', color: 'gold' },
    { cue: 'menu', label: t('a menu'), icon: 'text_bullet_list_tree_24_filled', color: 'indigo' },
    { cue: 'voice', label: t('your voice'), icon: 'mic_24_filled', color: 'teal' },
  ];
  const lift = ease(f, plan.from - 6, plan.from + 20);
  return (
    <AbsoluteFill>
      {/* The chores, one per sentence, each one repeating */}
      <div style={{ position: 'absolute', left: 0, right: 0, top: 540, transform: 'translateY(-50%)', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 26 }}>
        {CHORES.map((c, i) => {
          const at = repeat.seg(i);
          const k = pop(f, at - 4, { damping: 14, stiffness: 150 });
          const ticks = Math.max(0, Math.min(99, Math.floor((f - at) / 9) + 1));
          const away = sweep * (1 + i * 0.25);
          return (
            <div key={i} style={{
              width: 900, display: 'flex', alignItems: 'center', gap: 26, padding: '20px 30px', borderRadius: 24, boxSizing: 'border-box',
              background: 'rgba(30,30,36,0.92)', border: `1px solid ${C.line2}`, boxShadow: '0 20px 50px rgba(0,0,0,0.45)',
              opacity: Math.min(1, k * 1.4) * (1 - Math.min(1, away)), transform: `translateX(${(1 - k) * -160 + away * 900}px) rotate(${away * 6}deg)`,
              filter: away > 0.02 ? `blur(${away * 8}px)` : undefined,
            }}>
              <KdIcon icon={c.icon} color={c.color} size={72} />
              <div style={{ fontFamily: SANS, fontSize: 44, fontWeight: 700, color: C.text, whiteSpace: 'nowrap' }}>{t(c.label)}</div>
              <div style={{ marginLeft: 'auto', fontFamily: SANS, fontSize: 40, fontWeight: 800, color: C.accentText, fontVariantNumeric: 'tabular-nums' }}>
                ×{f >= at ? ticks : 0}
              </div>
            </div>
          );
        })}
      </div>
      {CHORES.map((_, i) => <Sfx key={i} at={repeat.seg(i) - 4} name="pop" volume={0.25} />)}
      <Sfx at={kd - 14} name="whoosh" volume={0.35} />

      {/* K-Desktop: the title card, then the three ways it listens */}
      <ShortTitle at={kd - 4} y={tween(f, plan.from - 6, plan.from + 20, 440, 360)} />
      <div style={{ position: 'absolute', left: 0, right: 0, top: 800, display: 'flex', justifyContent: 'center', gap: 28, opacity: 1 - ease(f, plan.from - 10, plan.from + 6, IN) }}>
        {ways.map(w => {
          const k = pop(f, repeat.cue(w.cue) - 4, { damping: 13, stiffness: 160 });
          return (
            <div key={w.cue} style={{
              display: 'flex', alignItems: 'center', gap: 14, padding: '12px 26px 12px 14px', borderRadius: 40, background: 'rgba(30,30,36,0.92)',
              border: '1.5px solid rgba(242,193,74,0.45)', opacity: Math.min(1, k * 1.4), transform: `translateY(${(1 - k) * 40}px) scale(${0.8 + 0.2 * k})`,
            }}>
              <KdIcon icon={w.icon} color={w.color} size={48} />
              <span style={{ fontFamily: SANS, fontSize: 32, fontWeight: 700, color: C.text }}>{w.label}</span>
              <Sfx at={repeat.cue(w.cue) - 4} name="pop-high" volume={0.18} />
            </div>
          );
        })}
      </div>

      {/* What this video covers */}
      {f > plan.from - 10 && (
        <ShortAgenda y={800 + (1 - lift) * 40} items={[
          { label: t('Get it running'), icon: 'arrow_download_24_filled', color: 'teal', at: plan.cue('get') },
          { label: t('A quick tour'), icon: 'compass_northwest_24_filled', color: 'blue', at: plan.cue('tour') },
          { label: t('Your first command'), icon: 'flash_24_filled', color: 'gold', at: plan.cue('first') },
        ]} />
      )}
    </AbsoluteFill>
  );
};
