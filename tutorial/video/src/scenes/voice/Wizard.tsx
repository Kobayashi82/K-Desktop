import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Fluent, KdIcon } from '../../components/Icon';
import { Mono } from '../../components/Keys';
import { Sfx } from '../../components/Sfx';
import { speechAmp, Wave } from '../../components/Wave';
import { ease, glide, pop } from '../../lib/anim';
import { BeatTimes } from '../../lib/timeline';
import { C, SANS } from '../../theme';
import { t } from '../../lib/i18n';

const STEPS = [t('Choose the word'), t('Silence'), t('Record the word'), t('Normal speech'), t('Try it')];

/** The wake word dialog (WakeWordDialog): five steps, recorded with your own voice. */
export const WizardPart: React.FC<{ own: BeatTimes }> = ({ own }) => {
  const f = useCurrentFrame();
  const at = [own.from, own.cue('silence'), own.cue('word'), own.cue('speech'), own.cue('madeup') - 10];
  const step = at.reduce((s, t, i) => (f >= t - 6 ? i : s), 0);
  const typed = 'KDesktop'.slice(0, Math.max(0, Math.floor((f - own.from - 10) / 3)));
  const silenceT = ease(f, at[1], at[1] + 90);
  const takes = [0, 1, 2, 3].map(i => f > at[2] + 12 + i * 14);
  const speechT = ease(f, at[3], at[4] - 16);
  const dialogIn = glide(f, own.from - 10, 26);
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 960, top: 545, transform: `translate(-50%, -50%) translateY(${(1 - dialogIn) * 80}px)`, width: 1300, height: 760, borderRadius: 28, background: 'rgba(30,30,36,0.98)', border: `1px solid ${C.line2}`, boxShadow: '0 40px 90px rgba(0,0,0,0.6)', overflow: 'hidden' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 16, padding: '28px 40px 0' }}>
          <KdIcon icon="mic_24_filled" color="teal" size={52} />
          <span style={{ fontFamily: SANS, fontSize: 34, fontWeight: 800, color: C.text }}>{t('Wake word')}</span>
        </div>
        {/* Stepper */}
        <div style={{ display: 'flex', gap: 12, padding: '26px 40px' }}>
          {STEPS.map((s, i) => (
            <div key={s} style={{ flex: 1 }}>
              <div style={{ height: 6, borderRadius: 3, background: i < step ? C.accentText : i === step ? `linear-gradient(90deg, ${C.accentText} 50%, rgba(255,255,255,0.12) 50%)` : 'rgba(255,255,255,0.12)' }} />
              <div style={{ marginTop: 10, fontFamily: SANS, fontSize: 19, fontWeight: i === step ? 800 : 500, color: i === step ? C.accentText : i < step ? C.text : C.faint }}>{s}</div>
            </div>
          ))}
        </div>
        <div style={{ position: 'absolute', left: 40, right: 40, top: 220, bottom: 40, opacity: step === 0 ? 1 : ease(f, at[step] - 6, at[step] + 6), transform: `translateX(${(1 - (step === 0 ? 1 : ease(f, at[step] - 6, at[step] + 8))) * 60}px)` }}>
          {step === 0 && (
            <div>
              <div style={{ fontFamily: SANS, fontSize: 26, color: C.dim, marginBottom: 22, lineHeight: 1.4 }}>{t('What do you want to say to call K-Desktop? It learns from your voice, so it can be a made-up word.')}</div>
              <div style={{ height: 90, borderRadius: 16, background: '#1a1a1f', border: `2px solid ${C.accentText}`, display: 'flex', alignItems: 'center', padding: '0 28px', fontFamily: SANS, fontSize: 46, fontWeight: 800, color: C.text }}>
                {typed}<span style={{ width: 3, height: 50, background: C.accentText, marginLeft: 4 }} />
              </div>
              <div style={{ marginTop: 20, fontFamily: SANS, fontSize: 22, color: C.faint }}>{t('Best with 2 to 4 syllables, and not something you say in normal conversation.')}</div>
            </div>
          )}
          {step === 1 && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 30 }}>
              <div style={{ fontFamily: SANS, fontSize: 30, color: C.text }}>{t('Stay quiet for 3 seconds so I can measure the background noise.')}</div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 30 }}>
                <div style={{ width: 140, height: 140, borderRadius: 70, display: 'flex', alignItems: 'center', justifyContent: 'center', border: `8px solid rgba(255,255,255,0.08)`, position: 'relative' }}>
                  <svg width="140" height="140" style={{ position: 'absolute', left: -8, top: -8 }}><circle cx="70" cy="70" r="62" stroke={C.teal} strokeWidth="8" fill="none" strokeDasharray={`${390 * silenceT} 390`} transform="rotate(-90 70 70)" strokeLinecap="round" /></svg>
                  <span style={{ fontFamily: SANS, fontSize: 52, fontWeight: 800, color: C.text }}>{Math.max(0, 3 - Math.floor(silenceT * 3))}</span>
                </div>
                <Wave width={800} height={110} color="rgba(62,220,193,0.6)" amp={() => 0.08} seed={9} />
              </div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 12, fontFamily: SANS, fontSize: 26, color: C.success, opacity: ease(f, at[1] + 80, at[1] + 90) }}>
                <Fluent name="checkmark_circle_24_filled" size={30} color={C.success} /> {t('Low background noise (−52 dB). Perfect.')}
              </div>
            </div>
          )}
          {step === 2 && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 26 }}>
              <div style={{ fontFamily: SANS, fontSize: 30, color: C.text }}>{t('Now (')}{Math.min(4, takes.filter(Boolean).length + 1)} {t('of 4): say')}{' '}<b style={{ color: C.accentText }}>{t('“KDesktop”')}</b></div>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 18 }}>
                {takes.map((t, i) => (
                  <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 16, padding: '16px 20px', borderRadius: 16, background: t ? 'rgba(108,203,95,0.10)' : 'rgba(255,255,255,0.04)', border: `1.5px solid ${t ? 'rgba(108,203,95,0.5)' : C.line}`, opacity: t ? 1 : 0.35, transform: `scale(${t ? pop(f, at[2] + 12 + i * 14) : 1})` }}>
                    <Fluent name={t ? 'checkmark_circle_24_filled' : 'circle_24_regular'} size={30} color={t ? C.success : C.faint} />
                    <Wave width={300} height={56} color={C.teal} amp={speechAmp(0.25, 0.75, 3)} scroll={false} seed={i * 3 + 1} />
                    <Mono size={22} color={C.dim}>{`“KDesktop” · 0.${7 + i} s`}</Mono>
                  </div>
                ))}
              </div>
            </div>
          )}
          {step === 3 && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 24 }}>
              <div style={{ fontFamily: SANS, fontSize: 28, color: C.text, lineHeight: 1.4 }}>{t('Now talk normally for about 10 seconds')}{' '}<b style={{ color: C.accentText }}>{t('without')}</b> {t('saying the word.')}</div>
              <div style={{ padding: 22, borderRadius: 16, background: 'rgba(255,255,255,0.04)', fontFamily: SANS, fontSize: 26, fontStyle: 'italic', color: C.dim, lineHeight: 1.5 }}>{t('“This morning I checked my email, tidied a few folders on the desktop and then put on some music while I was cooking.”')}</div>
              <div style={{ height: 120, borderRadius: 16, overflow: 'hidden', position: 'relative', background: 'rgba(255,255,255,0.03)' }}>
                <div style={{ width: `${speechT * 100}%`, overflow: 'hidden' }}><Wave width={1220} height={120} color={C.teal} amp={speechAmp(0, 1, 18)} scroll={false} seed={5} /></div>
              </div>
            </div>
          )}
          {step === 4 && (
            <div style={{ display: 'flex', alignItems: 'center', gap: 60, height: '100%' }}>
              <div style={{ transform: `scale(${pop(f, at[4])})`, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 16 }}>
                <div style={{ width: 200, height: 200, borderRadius: 100, background: 'rgba(108,203,95,0.15)', border: `4px solid ${C.success}`, display: 'flex', alignItems: 'center', justifyContent: 'center', boxShadow: `0 0 60px rgba(108,203,95,0.4)` }}>
                  <Fluent name="checkmark_24_filled" size={110} color={C.success} />
                </div>
                <span style={{ fontFamily: SANS, fontSize: 36, fontWeight: 800, color: C.success }}>{t('Detected')}</span>
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
                <Mono size={28} color={C.text}>{t('Detector 0.91 (needs 0.62)')}</Mono>
                <Mono size={28} color={C.text}>{t('Check 0.88 (needs 0.70)')}</Mono>
                <Mono size={28} color={C.accentText}>{t('Understood “KDesktop”')}</Mono>
              </div>
            </div>
          )}
        </div>
      </div>
      {at.slice(1).map((t, i) => <Sfx key={i} at={t - 6} name="whoosh-short" volume={0.18} />)}
      {takes.map((_, i) => <Sfx key={`t${i}`} at={at[2] + 12 + i * 14} name="blip" volume={0.15} />)}
      <Sfx at={at[4]} name="chime" volume={0.25} />
    </AbsoluteFill>
  );
};
