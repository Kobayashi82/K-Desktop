import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { Fluent } from '../../../src/components/Icon';
import { Phone } from '../../../src/components/Phone';
import { Sfx } from '../../../src/components/Sfx';
import { VoiceBars } from '../../../src/components/VoiceBars';
import { pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { BeatTimes, useBeats, useChapter } from '../../../src/lib/timeline';
import { TalkPart } from '../../../src/scenes/voice/Talk';
import { WizardPart } from '../../../src/scenes/voice/Wizard';
import { C, SANS } from '../../../src/theme';

/** The phone as the microphone: a drawn phone with the talk button, the web app or the Android app. */
const PhonePart: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const k = pop(f, b.cue('phone') - 10, { damping: 15 });
  const pulse = 1 + 0.06 * Math.sin(f / 5);
  const chip = (cue: string, label: string, y: number) => {
    const c = pop(f, b.cue(cue) - 6, { damping: 14 });
    return (
      <div style={{
        position: 'absolute', left: 1240, top: y, display: 'flex', alignItems: 'center', gap: 12, padding: '14px 26px', borderRadius: 30, fontFamily: SANS, fontSize: 32, fontWeight: 700,
        color: C.text, background: 'rgba(30,30,36,0.95)', border: `1.5px solid ${C.line2}`, opacity: Math.min(1, c * 1.4), transform: `translateX(${(1 - c) * 40}px)`,
      }}>
        <Fluent name={cue === 'android' ? 'phone_24_filled' : 'globe_24_filled'} size={34} color={C.teal} /> {label}
      </div>
    );
  };
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 760, top: 540, transform: `translate(-50%, -50%) scale(${0.8 + 0.2 * k})`, opacity: Math.min(1, k * 1.4) }}>
        <Phone width={400}>
          <div style={{ position: 'absolute', inset: 0, background: 'linear-gradient(180deg, #14161c, #0c0d10)', display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 50 }}>
            <VoiceBars beat="phone" bars={18} width={300} height={90} color={C.teal} gain={1.4} />
            <div style={{ width: 170, height: 170, borderRadius: 85, background: 'linear-gradient(180deg, #3EDCC1, #0C9A87)', display: 'flex', alignItems: 'center', justifyContent: 'center', transform: `scale(${pulse})`, boxShadow: '0 0 60px rgba(62,220,193,0.45)' }}>
              <Fluent name="mic_24_filled" size={86} color="white" />
            </div>
          </div>
        </Phone>
      </div>
      {chip('phone', t('Web app'), 400)}
      {chip('android', t('Android app'), 500)}
      <Sfx at={b.cue('phone') - 10} name="whoosh-short" volume={0.22} />
      <Sfx at={b.cue('android') - 6} name="pop" volume={0.2} />
    </AbsoluteFill>
  );
};

/** Chapter «Hands free»: hold or tap the key, the wake word, the phone, and recording your own word (the long video's scenes). */
export const Tip: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const phone = B('phone');
  const own = B('own');
  const wakewords = own.cue('wakewords');
  const wizardFrom = wakewords + 46;
  return (
    <AbsoluteFill>
      <Part from={0} to={phone.from - 6} first><TalkPart intro={B('intro')} two={B('two')} wake={B('wake')} /></Part>
      <Part from={phone.from - 6} to={own.from - 6}><PhonePart b={phone} /></Part>
      {/* Settings › Wake words, on the real page */}
      <Part from={own.from - 6} to={wizardFrom}>
        <CaptureShot name="settings-wake"
          shots={[{ at: own.from - 6, x: 960, y: 540, s: 1.0 }, { at: own.from, dur: 40, x: 760, y: 420, s: 1.35 }]}
          focus={[{ from: wakewords - 8, to: wizardFrom + 10, rect: [262, 170, 266, 48] }]} />
      </Part>
      <Part from={wizardFrom} to={durationInFrames} last><WizardPart own={own} /></Part>
    </AbsoluteFill>
  );
};
