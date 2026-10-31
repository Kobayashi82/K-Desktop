import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Fluent, KdIcon } from '../../../src/components/Icon';
import { ShortAgenda, ShortTitle } from '../../../src/components/Short';
import { Sfx } from '../../../src/components/Sfx';
import { VoiceBars } from '../../../src/components/VoiceBars';
import { ease, IN, keys, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';

/** A voice and a character, both adjustable; the title and what the video covers. */
export const Hook: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const idea = B('idea');
  const plan = B('plan');
  const voice = pop(f, idea.cue('voice') - 8, { damping: 13 });
  const who = pop(f, idea.cue('character') - 8, { damping: 13 });
  const tune = pop(f, idea.cue('change') - 6, { damping: 12 });
  const away = ease(f, idea.to - 10, idea.to + 10, IN);
  const tile = (k: number, x: number, children: React.ReactNode) => (
    <div style={{
      position: 'absolute', left: x, top: 450, transform: `translate(-50%, -50%) scale(${k})`, opacity: Math.min(1, k * 1.4),
      width: 520, height: 400, borderRadius: 30, background: 'rgba(30,30,36,0.94)', border: `1.5px solid ${C.line2}`, boxShadow: '0 30px 70px rgba(0,0,0,0.5)',
      display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 26, fontFamily: SANS,
    }}>{children}</div>
  );
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', inset: 0, opacity: 1 - away }}>
        {tile(voice, 650, <>
          <KdIcon icon="person_voice_24_filled" color="teal" size={110} />
          <VoiceBars beat="idea" bars={22} width={380} height={80} color={C.teal} gain={1.4} />
          <span style={{ fontSize: 34, fontWeight: 700, color: C.text }}>{t('Its voice')}</span>
        </>)}
        {tile(who, 1270, <>
          <KdIcon icon="emoji_24_filled" color="orange" size={110} />
          <div style={{ fontSize: 26, color: C.dim, fontStyle: 'italic', textAlign: 'center', width: 400 }}>{t('“Short, calm sentences, with a touch of humour.”')}</div>
          <span style={{ fontSize: 34, fontWeight: 700, color: C.text }}>{t('Its character')}</span>
        </>)}
        {[650, 1270].map(x => (
          <div key={x} style={{ position: 'absolute', left: x + 220, top: 270, transform: `scale(${tune})`, opacity: Math.min(1, tune * 1.4) }}>
            <div style={{ width: 70, height: 70, borderRadius: 35, background: 'linear-gradient(180deg, #FFD970, #F2B807)', display: 'flex', alignItems: 'center', justifyContent: 'center', boxShadow: '0 10px 30px rgba(0,0,0,0.5)' }}>
              <Fluent name="options_24_filled" size={38} color="#1a1306" />
            </div>
          </div>
        ))}
      </div>
      {f > idea.to - 6 && <ShortTitle at={idea.to - 2} y={keys(f, [[plan.from - 10, 440], [plan.from + 16, 380]])} />}
      {f > plan.from - 10 && (
        <ShortAgenda y={820} items={[
          { label: t('The voices'), icon: 'person_voice_24_filled', color: 'teal', at: plan.cue('voices') },
          { label: t('Voice profiles'), icon: 'sound_wave_circle_24_filled', color: 'purple', at: plan.cue('profiles') },
          { label: t('Personality'), icon: 'emoji_24_filled', color: 'orange', at: plan.cue('personality') },
        ]} />
      )}
      <Sfx at={idea.cue('voice') - 8} name="pop" volume={0.2} />
      <Sfx at={idea.cue('character') - 8} name="pop" volume={0.2} />
      <Sfx at={idea.cue('change') - 6} name="pop-high" volume={0.2} />
    </AbsoluteFill>
  );
};
