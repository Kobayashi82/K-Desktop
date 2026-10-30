import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { ease, glide, IN, OUT, pop, SMOOTH } from '../lib/anim';
import { t } from '../lib/i18n';
import { useBeats, useChapter, useTimeline } from '../lib/timeline';
import { C, SANS } from '../theme';
import { KdIcon } from './Icon';
import { Mono } from './Keys';
import { Sfx } from './Sfx';

/** «Tutorial 01»: the number of a short video, as its cards show it. */
const label = (n: number) => `${t('Tutorial')} ${String(n).padStart(2, '0')}`;

/**
 * The title card of a short video: the logo, «K-Desktop · Tutorial 01» and its title, written on from `at`; it rises
 * out of the way at `out`. `y` is where its centre sits.
 */
export const ShortTitle: React.FC<{ at: number; out?: number; y?: number }> = ({ at, out, y = 470 }) => {
  const f = useCurrentFrame();
  const { short } = useTimeline();
  if (!short) return null;
  const logo = pop(f, at, { damping: 13, stiffness: 120 });
  const kicker = ease(f, at + 8, at + 24, OUT);
  const wipe = ease(f, at + 12, at + 34, OUT);
  const bar = ease(f, at + 20, at + 44, OUT);
  const gone = out !== undefined ? ease(f, out, out + 18, IN) : 0;
  // Long titles get smaller, so they always fit in one line.
  const size = Math.min(132, 1700 / (0.56 * short.title.length));
  return (
    <div style={{
      position: 'absolute', left: 0, right: 0, top: y, transform: `translateY(-50%) translateY(${-gone * 60}px) scale(${1 - gone * 0.06})`,
      display: 'flex', flexDirection: 'column', alignItems: 'center', opacity: 1 - gone, filter: gone > 0.02 ? `blur(${gone * 10}px)` : undefined,
    }}>
      <Img src={staticFile('brand/icon-192.png')} style={{
        width: 150, height: 150, transform: `scale(${logo}) rotate(${(1 - logo) * -20}deg)`, filter: 'drop-shadow(0 18px 50px rgba(224,168,21,0.5))',
      }} />
      <div style={{
        marginTop: 34, fontFamily: SANS, fontSize: 28, fontWeight: 700, letterSpacing: 6, textTransform: 'uppercase', color: C.accentText,
        opacity: kicker, transform: `translateY(${(1 - kicker) * 16}px)`,
      }}>
        {t('K-Desktop')} <span style={{ color: C.faint }}>·</span> {label(short.number)}
      </div>
      <div style={{ overflow: 'hidden', paddingBottom: 12, marginTop: 6 }}>
        <div style={{
          fontFamily: SANS, fontSize: size, fontWeight: 800, letterSpacing: -size * 0.03, lineHeight: 1.05, color: C.text, whiteSpace: 'nowrap',
          clipPath: `inset(0 ${100 - wipe * 100}% 0 0)`, transform: `translateX(${(1 - wipe) * -30}px)`,
        }}>{short.title}</div>
      </div>
      <div style={{ width: 520 * bar, height: 6, borderRadius: 3, background: `linear-gradient(90deg, ${C.accent}, ${C.gold1})`, boxShadow: '0 0 20px rgba(242,193,74,0.5)' }} />
      <Sfx at={at} name="impact" volume={0.3} />
      <Sfx at={at + 12} name="whoosh" volume={0.25} />
    </div>
  );
};

/** What the video covers: numbered steps that light up as the narrator names them (`at`), in a row. */
export const ShortAgenda: React.FC<{ items: { label: string; icon: string; color: string; at: number }[]; y?: number; out?: number }> = ({ items, y = 820, out }) => {
  const f = useCurrentFrame();
  const gone = out !== undefined ? ease(f, out, out + 14, IN) : 0;
  return (
    <div style={{ position: 'absolute', left: 0, right: 0, top: y, display: 'flex', justifyContent: 'center', gap: 34, opacity: 1 - gone, transform: 'translateY(-50%)' }}>
      {items.map((it, i) => {
        const k = pop(f, it.at - 6, { damping: 14, stiffness: 150 });
        const lit = ease(f, it.at - 4, it.at + 8);
        return (
          <div key={i} style={{
            display: 'flex', alignItems: 'center', gap: 18, padding: '18px 30px 18px 18px', borderRadius: 22,
            background: 'rgba(30,30,36,0.92)', border: `1.5px solid rgba(242,193,74,${0.15 + 0.5 * lit})`,
            boxShadow: `0 20px 50px rgba(0,0,0,0.45), 0 0 ${40 * lit}px rgba(242,193,74,${0.18 * lit})`,
            opacity: Math.min(1, k * 1.4), transform: `translateY(${(1 - k) * 60}px) scale(${0.8 + 0.2 * k})`,
          }}>
            <KdIcon icon={it.icon} color={it.color} size={64} />
            <div>
              <div style={{ fontFamily: SANS, fontSize: 18, fontWeight: 700, letterSpacing: 2, color: C.accentText }}>{String(i + 1).padStart(2, '0')}</div>
              <div style={{ fontFamily: SANS, fontSize: 32, fontWeight: 700, color: C.text, whiteSpace: 'nowrap' }}>{it.label}</div>
            </div>
            <Sfx at={it.at - 6} name="pop" volume={0.25} />
          </div>
        );
      })}
    </div>
  );
};

/** The last chapter of a short video (beats «next» and «thanks»): what comes next, where to get K-Desktop, thanks. */
export const ShortEnd: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { short } = useTimeline();
  const { durationInFrames } = useChapter();
  const next = B('next');
  const thanks = B('thanks');
  const logo = pop(f, 2, { damping: 13, stiffness: 120 });
  const card = glide(f, next.cue('next') - 10, 22);
  const fade = ease(f, durationInFrames - 40, durationInFrames - 4, SMOOTH);
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 960, top: 250, transform: `translate(-50%, -50%) scale(${logo})` }}>
        <Img src={staticFile('brand/icon-512.png')} style={{ width: 190, height: 190, filter: 'drop-shadow(0 20px 60px rgba(224,168,21,0.55))' }} />
      </div>
      {short?.next && (
        <div style={{
          position: 'absolute', left: 960, top: 520, transform: `translate(-50%, -50%) translateY(${(1 - card) * 60}px)`, opacity: card,
          display: 'flex', alignItems: 'center', gap: 30, padding: '30px 46px', borderRadius: 30,
          background: 'linear-gradient(180deg, rgba(44,44,52,0.97), rgba(30,30,36,0.97))', border: '1.5px solid rgba(242,193,74,0.5)',
          boxShadow: '0 30px 80px rgba(0,0,0,0.55), 0 0 60px rgba(242,193,74,0.15)',
        }}>
          <div style={{ fontFamily: SANS, fontWeight: 800, fontSize: 110, lineHeight: 1, letterSpacing: -4, color: 'transparent', WebkitTextStroke: `3px ${C.accentText}` }}>
            {String(short.nextNumber ?? short.number + 1).padStart(2, '0')}
          </div>
          <div style={{ width: 5, alignSelf: 'stretch', borderRadius: 3, background: `linear-gradient(180deg, ${C.gold1}, ${C.accent})` }} />
          <div>
            <div style={{ fontFamily: SANS, fontSize: 24, fontWeight: 700, letterSpacing: 5, textTransform: 'uppercase', color: C.accentText }}>{t('Next up')}</div>
            <div style={{ fontFamily: SANS, fontSize: 64, fontWeight: 800, letterSpacing: -1.5, color: C.text, whiteSpace: 'nowrap' }}>{short.next}</div>
          </div>
        </div>
      )}
      <div style={{ position: 'absolute', left: 0, right: 0, top: 730, display: 'flex', justifyContent: 'center', opacity: glide(f, next.to - 4, 20) }}>
        <div style={{ padding: '14px 30px', borderRadius: 16, background: 'rgba(255,255,255,0.07)', border: `1px solid ${C.line2}` }}>
          <Mono size={30} color={C.text}>{t('github.com/Kobayashi82/K-Desktop')}</Mono>
        </div>
      </div>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 830, textAlign: 'center', fontFamily: SANS, fontSize: 34, fontWeight: 700, color: C.dim, opacity: ease(f, thanks.from - 6, thanks.from + 10) }}>{t('Thanks for watching')}</div>
      <AbsoluteFill style={{ background: `rgba(0,0,0,${fade})` }} />
      <Sfx at={2} name="impact" volume={0.3} />
      <Sfx at={next.cue('next') - 10} name="whoosh-short" volume={0.3} />
      <Sfx at={next.to - 4} name="chime" volume={0.2} />
    </AbsoluteFill>
  );
};
