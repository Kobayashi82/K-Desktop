import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { Fluent } from '../../../src/components/Icon';
import { Sfx } from '../../../src/components/Sfx';
import { glide, inOut } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';
import { blips } from './Voices';

/** The Lander's settings (lander-settings.png) and its «Personality» tab. */
const Tabs: React.FC = () => {
  const B = useBeats();
  const x = B('text');
  const focus: Focus[] = [{ from: x.cue('tab') - 6, to: x.cue('who') + 10, rect: [652, 218, 107, 28], label: t('Personality'), at: 'bottom' }];
  return (
    <>
      <CaptureShot name="lander-settings" focus={focus}
        shots={[{ at: 0, x: 960, y: 540, s: 0.96, rx: 6 }, { at: 4, dur: 30, x: 960, y: 540, s: 1.0, rx: 0 }, { at: x.cue('tab') - 18, dur: 26, x: 900, y: 260, s: 1.6 }]} />
      {blips(focus)}
    </>
  );
};

// The Lander's own personality (assistants/Lander, Package/persona/en.json).
const PERSONA = t('You are the Lander, a small lunar module that lives on the desktop and lands on windows. You speak naturally, in short, calm sentences, with a touch of humour. You are a pilot and it shows now and then, but as a rare touch, not a habit. Get straight to the point. Never more than two sentences.');

const Row: React.FC<{ label: string; hint?: string; value: string; lit: number }> = ({ label, hint, value, lit }) => (
  <div style={{ display: 'flex', alignItems: 'center', gap: 20, padding: '14px 0', borderTop: `1px solid ${C.line}` }}>
    <div style={{ flex: 1 }}>
      <div style={{ fontSize: 24, fontWeight: 600, color: C.text }}>{label}</div>
      {hint && <div style={{ fontSize: 18, color: C.dim, marginTop: 4 }}>{hint}</div>}
    </div>
    <div style={{
      width: 360, height: 50, display: 'flex', alignItems: 'center', padding: '0 16px', borderRadius: 8, fontSize: 21, color: C.text, boxSizing: 'border-box',
      background: 'rgba(255,255,255,0.06)', border: `1.5px solid rgba(242,193,74,${0.1 + 0.8 * lit})`, boxShadow: lit ? `0 0 ${24 * lit}px rgba(242,193,74,${0.3 * lit})` : undefined,
    }}>
      {value}
      <Fluent name="chevron_down_24_regular" size={20} color={C.dim} style={{ marginLeft: 'auto' }} />
    </div>
  </div>
);

/** The «Personality» and «Voice» tabs, drawn with their real texts. */
const Card: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const x = B('text');
  const l = B('lang');
  const card = glide(f, x.cue('who') - 4, 22);
  const edit = inOut(f, x.cue('change') - 4, x.cue('reset') - 2, 6);
  const reset = inOut(f, x.cue('reset') - 4, l.from + 10, 6);
  const voice = f >= l.cue('voice') - 8;
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <div style={{
        position: 'absolute', left: 960, top: 520, width: 1240, padding: '30px 36px', borderRadius: 26, boxSizing: 'border-box', transform: `translate(-50%, -50%) translateY(${(1 - card) * 60}px)`, opacity: card,
        background: 'linear-gradient(180deg, rgba(44,44,52,0.98), rgba(30,30,36,0.98))', border: `1.5px solid ${C.line2}`, boxShadow: '0 30px 80px rgba(0,0,0,0.55)',
      }}>
        {!voice ? (
          <>
            <div style={{ display: 'flex', alignItems: 'center' }}>
              <div>
                <div style={{ fontSize: 32, fontWeight: 700, color: C.text }}>{t('Personality')}</div>
                <div style={{ fontSize: 20, color: C.dim, marginTop: 6 }}>{t('What tells the AI who it is and how it talks. Change it as you like.')}</div>
              </div>
              <div style={{
                marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: 10, padding: '10px 20px', borderRadius: 8, fontSize: 20, color: C.text,
                background: `rgba(255,255,255,${0.07 + 0.15 * reset})`, border: `1.5px solid rgba(242,193,74,${0.8 * reset})`,
              }}><Fluent name="arrow_counterclockwise_24_regular" size={22} color={C.text} /> {t('Reset')}</div>
            </div>
            <div style={{
              marginTop: 20, padding: '18px 22px', borderRadius: 12, fontSize: 22, lineHeight: 1.5, color: C.text, background: 'rgba(0,0,0,0.25)',
              border: `1.5px solid rgba(242,193,74,${0.15 + 0.7 * edit})`,
            }}>{PERSONA}{edit > 0.5 && <span style={{ opacity: f % 30 < 15 ? 1 : 0 }}>▍</span>}</div>
            <div style={{ fontSize: 18, color: C.dim, marginTop: 10 }}>{t('The one of its package, in English.')}</div>
            <div style={{ marginTop: 18 }}>
              <Row label={t('Language')} hint={t('The one it answers and speaks in. Changing it makes it come onto the desktop again.')} value={t('Global (English)')} lit={inOut(f, l.cue('language') - 4, l.cue('voice') - 8, 6)} />
            </div>
          </>
        ) : (
          <>
            <div style={{ fontSize: 32, fontWeight: 700, color: C.text }}>{t('Voice')}</div>
            <div style={{ fontSize: 20, color: C.dim, marginTop: 6, marginBottom: 16 }}>{t('«Global» is what you have in Assistant module › Voice.')}</div>
            <Row label={t('How they sound')} value={t('The one its package brings')} lit={inOut(f, l.cue('voice') - 4, l.cue('speed') - 4, 6)} />
            <Row label={t('Its own speed')} value="1.00×" lit={inOut(f, l.cue('speed') - 4, l.to + 40, 6)} />
            <Row label={t('Own volume')} value="100 %" lit={inOut(f, l.cue('speed') + 4, l.to + 40, 6)} />
          </>
        )}
      </div>
      <Sfx at={x.cue('change') - 4} name="key" volume={0.2} />
      <Sfx at={x.cue('reset') - 4} name="click" volume={0.2} />
      <Sfx at={l.cue('language') - 4} name="blip" volume={0.14} />
      <Sfx at={l.cue('voice') - 8} name="whoosh-short" volume={0.2} />
    </AbsoluteFill>
  );
};

/** Chapter «Personality». */
export const Personality: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const x = B('text');
  return (
    <AbsoluteFill>
      <Part from={0} to={x.cue('who') - 6} first><Tabs /></Part>
      <Part from={x.cue('who') - 6} to={durationInFrames} last><Card /></Part>
    </AbsoluteFill>
  );
};
