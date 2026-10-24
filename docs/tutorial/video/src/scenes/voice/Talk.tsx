import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { AppWindow, Wallpaper } from '../../components/Desktop';
import { Fluent, KdIcon } from '../../components/Icon';
import { Keycap } from '../../components/Keys';
import { Osd } from '../../components/Osd';
import { Sfx } from '../../components/Sfx';
import { VoiceBars } from '../../components/VoiceBars';
import { speechAmp, Wave } from '../../components/Wave';
import { ease, glide, inOut, pop } from '../../lib/anim';
import { BeatTimes } from '../../lib/timeline';
import { C, SANS } from '../../theme';
import { t } from '../../lib/i18n';

const Chip: React.FC<{ icon: string; text: string; show: number }> = ({ icon, text, show }) => (
  <div style={{ display: 'flex', alignItems: 'center', gap: 14, padding: '16px 28px', borderRadius: 40, background: 'rgba(34,34,40,0.96)', border: `1.5px solid ${C.line2}`, opacity: Math.min(1, show * 2), transform: `translateY(${(1 - show) * 40}px)` }}>
    <Fluent name={icon} size={34} color={C.teal} />
    <span style={{ fontFamily: SANS, fontSize: 30, fontWeight: 700, color: C.text }}>{text}</span>
  </div>
);

/** «Now, the voice»; holding or tapping the key; the wake word. */
export const TalkPart: React.FC<{ intro: BeatTimes; two: BeatTimes; wake: BeatTimes }> = ({ intro, two, wake }) => {
  const f = useCurrentFrame();
  const micIn = pop(f, intro.cue('voice') - 6, { damping: 12 });
  const toTwo = glide(f, two.from - 8, 24);
  const toWake = glide(f, wake.from - 8, 24);
  const holdAt = two.cue('hold');
  const tapAt = two.cue('tap');
  const stopsAt = two.cue('stops');
  // The two ways, as two timelines.
  const holdT = ease(f, holdAt, holdAt + 60);
  const tapT = ease(f, tapAt, stopsAt + 20);
  const browser = pop(f, wake.cue('kd') + 40);
  const listening = inOut(f, wake.cue('kd') + 6, wake.cue('kd') + 44, 6);
  return (
    <AbsoluteFill>
      {/* The microphone */}
      <AbsoluteFill style={{ opacity: 1 - toTwo, transform: `scale(${1 + 0.2 * toTwo})` }}>
        <div style={{ position: 'absolute', left: 960, top: 380, transform: `translate(-50%, -50%) scale(${micIn})`, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 40 }}>
          <KdIcon icon="mic_24_filled" color="teal" size={220} glow={0.7} />
          <VoiceBars beat="intro" bars={30} width={560} height={130} color={C.teal} gain={1.5} />
        </div>
        <div style={{ position: 'absolute', left: 0, right: 0, top: 700, display: 'flex', justifyContent: 'center', gap: 30 }}>
          <Chip icon="desktop_24_filled" text={t('On your PC')} show={pop(f, intro.cue('local') - 6)} />
          <Chip icon="cloud_off_24_filled" text={t('No internet')} show={pop(f, intro.cue('internet') - 6)} />
        </div>
      </AbsoluteFill>

      {/* Hold, or tap */}
      <AbsoluteFill style={{ opacity: toTwo * (1 - toWake), transform: `scale(${(0.9 + 0.1 * toTwo) * (1 + 0.15 * toWake)})` }}>
        {[
          { title: t('Hold the key'), sub: t('while you speak'), t: holdT, at: holdAt, down: inOut(f, holdAt, holdAt + 60, 4), y: 260, amp: speechAmp(0.08, 0.92, 6), end: t('You let go') },
          { title: t('Tap it once'), sub: t('it stops when you go quiet'), t: tapT, at: tapAt, down: inOut(f, tapAt, tapAt + 8, 3), y: 590, amp: speechAmp(0.08, 0.7, 5), end: t('Silence: done') },
        ].map((r, i) => (
          <div key={i} style={{ position: 'absolute', left: 170, top: r.y, display: 'flex', alignItems: 'center', gap: 50, opacity: ease(f, r.at - 14, r.at) * 0.7 + 0.3 }}>
            <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 16, width: 300 }}>
              <Keycap label={<Fluent name="mic_24_filled" size={50} color={r.down > 0.5 ? '#1a1306' : C.text} />} height={130} width={150} down={r.down} />
              <div style={{ fontFamily: SANS, fontSize: 32, fontWeight: 800, color: C.text }}>{r.title}</div>
              <div style={{ fontFamily: SANS, fontSize: 22, color: C.dim, textAlign: 'center' }}>{r.sub}</div>
            </div>
            <div style={{ position: 'relative', width: 1150, height: 170, borderRadius: 20, background: 'rgba(255,255,255,0.04)', border: `1px solid ${C.line}`, overflow: 'hidden' }}>
              <div style={{ position: 'absolute', left: 0, top: 0, width: `${r.t * 100}%`, height: '100%', overflow: 'hidden' }}>
                <Wave width={1150} height={170} color={C.teal} amp={r.amp} scroll={false} seed={i + 4} />
              </div>
              {i === 0 && <div style={{ position: 'absolute', left: 0, top: 0, bottom: 0, width: `${r.down > 0.1 ? r.t * 100 : 0}%`, background: 'rgba(242,193,74,0.10)', borderRight: `3px solid ${C.accentText}` }} />}
              {i === 1 && f > stopsAt && (
                <>
                  <div style={{ position: 'absolute', left: '70%', top: 0, bottom: 0, width: '18%', background: 'repeating-linear-gradient(45deg, rgba(255,255,255,0.05) 0 10px, transparent 10px 20px)' }} />
                  <div style={{ position: 'absolute', left: '88%', top: 0, bottom: 0, width: 3, background: C.accentText, opacity: ease(f, stopsAt + 14, stopsAt + 20) }} />
                </>
              )}
              <div style={{ position: 'absolute', right: 20, top: 14, fontFamily: SANS, fontSize: 20, fontWeight: 700, color: C.accentText, opacity: r.t > 0.95 ? 1 : 0 }}>{r.end}</div>
            </div>
          </div>
        ))}
      </AbsoluteFill>

      {/* The wake word */}
      <AbsoluteFill style={{ opacity: toWake }}>
        <div style={{ position: 'absolute', inset: 0, opacity: 0.55 }}>
          <Wallpaper hue={-40} />
          <div style={{ position: 'absolute', left: 980, top: 140, transform: `scale(${browser})`, opacity: Math.min(1, browser * 2) }}>
            <AppWindow kind="browser" width={820} height={600} style={{ position: 'relative' }} />
          </div>
        </div>
        <div style={{ position: 'absolute', left: 160, top: 330, transform: `scale(${pop(f, wake.cue('kd') - 8, { damping: 13 })})`, transformOrigin: 'left center' }}>
          <div style={{ padding: '30px 44px', borderRadius: 40, background: 'white', fontFamily: SANS, fontSize: 60, fontWeight: 800, color: '#141418', boxShadow: '0 30px 70px rgba(0,0,0,0.5)', whiteSpace: 'nowrap' }}>
            <span style={{ color: '#C98A00' }}>{t('K-Desktop')}</span>{t(', open the browser')}
          </div>
          <div style={{ position: 'absolute', left: 60, bottom: -26, width: 50, height: 50, background: 'white', transform: 'rotate(45deg)' }} />
        </div>
        <div style={{ position: 'absolute', left: 170, top: 520, display: 'flex', alignItems: 'center', gap: 16, fontFamily: SANS, fontSize: 26, color: C.accentText, fontWeight: 700, opacity: ease(f, wake.cue('kd') + 4, wake.cue('kd') + 12) }}>
          <Fluent name="arrow_up_24_filled" size={30} color={C.accentText} /> {t('the wake word')}
        </div>
        <div style={{ position: 'absolute', left: 170, top: 640, transform: 'scale(1.1)', transformOrigin: 'left top' }}>
          <Osd icon="mic_24_filled" color="teal" text={t('Listening…')} show={listening} />
        </div>
        <div style={{ position: 'absolute', left: 170, top: 800, display: 'flex', alignItems: 'center', gap: 20, opacity: pop(f, wake.cue('nokey') - 6) }}>
          <div style={{ position: 'relative' }}>
            <Keycap label={<Fluent name="mic_24_filled" size={40} color={C.text} />} height={100} width={120} dim />
            <div style={{ position: 'absolute', left: -10, top: 46, width: 140, height: 6, borderRadius: 3, background: C.danger, transform: 'rotate(-30deg)' }} />
          </div>
          <span style={{ fontFamily: SANS, fontSize: 36, fontWeight: 800, color: C.text }}>{t('No key at all')}</span>
        </div>
      </AbsoluteFill>
      <Sfx at={intro.cue('voice') - 6} name="impact" volume={0.25} />
      <Sfx at={holdAt} name="key" volume={0.4} />
      <Sfx at={tapAt} name="key" volume={0.4} />
      <Sfx at={stopsAt + 16} name="blip" volume={0.2} />
      <Sfx at={wake.cue('kd') - 8} name="pop" volume={0.3} />
      <Sfx at={wake.cue('kd') + 6} name="blip-low" volume={0.25} />
      <Sfx at={wake.cue('kd') + 40} name="pop" volume={0.25} />
    </AbsoluteFill>
  );
};
