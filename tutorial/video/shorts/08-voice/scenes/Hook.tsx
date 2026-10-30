import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { KdIcon } from '../../../src/components/Icon';
import { ShortAgenda, ShortTitle } from '../../../src/components/Short';
import { Sfx } from '../../../src/components/Sfx';
import { VoiceBars } from '../../../src/components/VoiceBars';
import { ease, IN, keys, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';

/** A microphone that moves with the narrator's voice, the title, and what the video covers. */
export const Hook: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const idea = B('idea');
  const plan = B('plan');
  const mic = pop(f, idea.cue('say') - 8, { damping: 12, stiffness: 120 });
  const micOut = ease(f, idea.to - 10, idea.to + 10, IN);
  const titleAt = idea.to - 4;
  const local = pop(f, idea.cue('local') - 6, { damping: 14 });
  return (
    <AbsoluteFill>
      <div style={{
        position: 'absolute', left: 960, top: 430, transform: `translate(-50%, -50%) scale(${mic * (1 - 0.3 * micOut)})`, opacity: 1 - micOut,
        display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 40,
      }}>
        <KdIcon icon="mic_24_filled" color="teal" size={220} glow={0.7} />
        <VoiceBars beat="idea" bars={30} width={560} height={120} color={C.teal} gain={1.5} />
      </div>
      <div style={{
        position: 'absolute', left: 0, right: 0, top: 780, display: 'flex', justifyContent: 'center', opacity: Math.min(1, local * 1.4) * (1 - micOut),
        transform: `translateY(${(1 - local) * 30}px)`,
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '12px 26px', borderRadius: 40, background: 'rgba(30,30,36,0.92)', border: '1.5px solid rgba(242,193,74,0.45)' }}>
          <KdIcon icon="desktop_24_filled" color="blue" size={40} />
          <span style={{ fontFamily: SANS, fontSize: 32, fontWeight: 700, color: C.text }}>{t('On your PC')}</span>
        </div>
      </div>
      {f > titleAt - 2 && <ShortTitle at={titleAt} y={keys(f, [[plan.from - 10, 440], [plan.from + 16, 380]])} />}
      {f > plan.from - 10 && (
        <ShortAgenda y={820} items={[
          { label: t('Set it up'), icon: 'settings_24_filled', color: 'gray', at: plan.cue('setup') },
          { label: t('Commands'), icon: 'flash_24_filled', color: 'gold', at: plan.cue('commands') },
          { label: t('Dictation'), icon: 'mic_sparkle_24_filled', color: 'teal', at: plan.cue('dictate') },
          { label: t('Hands free'), icon: 'hand_wave_24_filled', color: 'purple', at: plan.cue('hands') },
        ]} />
      )}
      <Sfx at={idea.cue('say') - 8} name="pop" volume={0.25} />
      <Sfx at={idea.cue('local') - 6} name="pop-high" volume={0.18} />
    </AbsoluteFill>
  );
};
