import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { KdIcon } from '../../../src/components/Icon';
import { Keycap } from '../../../src/components/Keys';
import { MouseShape } from '../../../src/components/Props';
import { Sfx } from '../../../src/components/Sfx';
import { ease, inOut, OUT, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';

/** A small screen whose top-left corner the pointer runs into, lighting it. */
const Corner: React.FC<{ k: number; lit: number }> = ({ k, lit }) => {
  const x = 230 - 200 * ease(k, 0, 1, OUT);
  const y = 150 - 120 * ease(k, 0, 1, OUT);
  return (
    <svg width="300" height="210" viewBox="0 0 300 210" style={{ overflow: 'visible' }}>
      <rect x="10" y="10" width="280" height="170" rx="12" fill="#1c2030" stroke="rgba(255,255,255,0.25)" strokeWidth="3" />
      <rect x="125" y="182" width="50" height="16" fill="#2a2e3c" />
      <path d="M 10 70 L 10 22 Q 10 10 22 10 L 70 10" fill="none" stroke="#F2C14A" strokeWidth="8" strokeLinecap="round" opacity={lit}
        style={{ filter: `drop-shadow(0 0 ${12 * lit}px #F2C14A)` }} />
      <path transform={`translate(${x} ${y}) scale(1.6)`} d="M1 1 L1 18.5 L5.4 14.4 L8.3 21.2 L11.3 19.9 L8.5 13.3 L14.6 13.3 Z" fill="white" stroke="black" strokeWidth="1.1" />
    </svg>
  );
};

/** A mouse gesture: a stroke drawn by the pointer (down, then right). */
const Gesture: React.FC<{ k: number }> = ({ k }) => {
  const len = 360;
  const p = Math.min(1, k);
  const [x, y] = p < 0.5 ? [60, 30 + 300 * p] : [60 + 340 * (p - 0.5), 180];
  return (
    <svg width="300" height="210" viewBox="0 0 300 210" style={{ overflow: 'visible' }}>
      <path d="M 60 30 L 60 180 L 230 180" fill="none" stroke="rgba(255,255,255,0.12)" strokeWidth="14" strokeLinecap="round" strokeLinejoin="round" />
      <path d="M 60 30 L 60 180 L 230 180" fill="none" stroke="#F2C14A" strokeWidth="14" strokeLinecap="round" strokeLinejoin="round"
        strokeDasharray={`${len * p} ${len}`} style={{ filter: 'drop-shadow(0 0 10px rgba(242,193,74,0.7))' }} />
      <path transform={`translate(${Math.min(x, 230) - 3} ${y - 2}) scale(1.6)`} d="M1 1 L1 18.5 L5.4 14.4 L8.3 21.2 L11.3 19.9 L8.5 13.3 L14.6 13.3 Z" fill="white" stroke="black" strokeWidth="1.1" />
    </svg>
  );
};

/** Chapter «One more thing»: a hotkey can be a mouse button, a screen corner or a mouse gesture. */
export const Tip: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const tip = B('tip');
  const bulb = pop(f, tip.from - 8, { damping: 12, stiffness: 140 });
  const bulbOut = ease(f, tip.seg(1) - 12, tip.seg(1) + 2);
  const keyIn = pop(f, tip.seg(1) - 6, { damping: 14 });
  const keyUp = ease(f, tip.seg(2) - 10, tip.seg(2) + 14);
  const cards = [
    { cue: tip.cue('mouse'), label: t('Mouse button') },
    { cue: tip.cue('corner'), label: t('Screen corner') },
    { cue: tip.cue('gesture'), label: t('Mouse gesture') },
  ];
  return (
    <AbsoluteFill>
      {/* «One last tip» */}
      <div style={{
        position: 'absolute', left: 0, right: 0, top: 470, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 22,
        opacity: Math.min(1, bulb * 1.4) * (1 - bulbOut), transform: `translateY(-50%) scale(${(0.7 + 0.3 * bulb) * (1 - 0.2 * bulbOut)})`,
      }}>
        <KdIcon icon="lightbulb_24_filled" color="gold" size={170} glow={0.8} />
        <div style={{ fontFamily: SANS, fontSize: 64, fontWeight: 800, letterSpacing: -1, color: C.text }}>{t('Tip')}</div>
      </div>
      {/* «A hotkey doesn't have to be a key» */}
      <div style={{
        position: 'absolute', left: 0, right: 0, top: 420 - 250 * keyUp, display: 'flex', justifyContent: 'center', alignItems: 'center', gap: 20,
        opacity: Math.min(1, keyIn * 1.4) * (1 - 0.6 * keyUp), transform: `scale(${(0.8 + 0.2 * keyIn) * (1 - 0.35 * keyUp)})`,
      }}>
        {['Ctrl', 'Alt', 'N'].map((k, i) => (
          <React.Fragment key={k}>
            {i > 0 && <span style={{ fontFamily: SANS, fontSize: 50, fontWeight: 700, color: C.faint }}>+</span>}
            <Keycap label={k} height={120} width={k.length > 1 ? 190 : 120} size={40} />
          </React.Fragment>
        ))}
      </div>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 360, display: 'flex', justifyContent: 'center', gap: 60 }}>
        {cards.map((c, i) => {
          const k = pop(f, c.cue - 6, { damping: 14, stiffness: 150 });
          const lit = inOut(f, c.cue - 2, c.cue + 50, 6);
          return (
            <div key={i} style={{
              width: 420, height: 440, borderRadius: 28, background: 'rgba(34,34,40,0.96)', border: `1.5px solid ${lit > 0.1 ? 'rgba(242,193,74,0.7)' : C.line2}`,
              display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'space-between', padding: '36px 0 30px', boxSizing: 'border-box',
              opacity: Math.min(1, k * 1.5), transform: `translateY(${(1 - k) * 120}px) scale(${0.75 + 0.25 * k})`,
              boxShadow: lit > 0.1 ? `0 0 50px rgba(242,193,74,${0.25 * lit})` : '0 20px 50px rgba(0,0,0,0.5)',
            }}>
              <div style={{ height: 300, display: 'flex', alignItems: 'center' }}>
                {i === 0 && <MouseShape scale={0.78} side={inOut(f, c.cue + 4, c.cue + 14, 3)} glow={lit} />}
                {i === 1 && <Corner k={ease(f, c.cue - 4, c.cue + 14)} lit={lit} />}
                {i === 2 && <Gesture k={ease(f, c.cue - 2, c.cue + 26)} />}
              </div>
              <div style={{ fontFamily: SANS, fontSize: 32, fontWeight: 700, color: C.accentText }}>{c.label}</div>
            </div>
          );
        })}
      </div>
      <Sfx at={tip.from - 8} name="chime" volume={0.25} />
      <Sfx at={tip.seg(1) - 6} name="pop-low" volume={0.25} />
      {cards.map(c => <Sfx key={c.cue} at={c.cue - 6} name="pop" volume={0.25} />)}
    </AbsoluteFill>
  );
};
