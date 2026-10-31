import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { KdIcon } from '../../../src/components/Icon';
import { ShortAgenda, ShortTitle } from '../../../src/components/Short';
import { Sfx } from '../../../src/components/Sfx';
import { ease, IN, keys, OUT, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';

/** Three things an assistant does, gathered into its tools; the title and what the video covers. */
export const Hook: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const idea = B('idea');
  const plan = B('plan');
  const tiles = [
    { cue: 'files', icon: 'folder_24_filled', color: 'yellow', label: t('Find files') },
    { cue: 'weather', icon: 'weather_partly_cloudy_day_24_filled', color: 'sky', label: t('Weather') },
    { cue: 'music', icon: 'play_circle_24_filled', color: 'pink', label: t('Control playback') },
  ];
  const gather = ease(f, idea.cue('tools') - 10, idea.cue('tools') + 8, IN);
  const wrench = pop(f, idea.cue('tools') + 2, { damping: 12 });
  const away = ease(f, idea.to + 4, idea.to + 20, IN);
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', inset: 0, opacity: 1 - away }}>
        {tiles.map((x, i) => {
          const k = pop(f, idea.cue(x.cue) - 6, { damping: 13 });
          const dx = (i - 1) * 440 * (1 - gather);
          return (
            <div key={x.cue} style={{
              position: 'absolute', left: 960 + dx, top: 470, transform: `translate(-50%, -50%) scale(${k * (1 - 0.6 * gather)})`, opacity: Math.min(1, k * 1.4) * (1 - gather),
              display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 22, padding: '34px 40px', borderRadius: 28, width: 340, boxSizing: 'border-box',
              background: 'rgba(30,30,36,0.94)', border: `1.5px solid ${C.line2}`, boxShadow: '0 30px 70px rgba(0,0,0,0.5)',
            }}>
              <KdIcon icon={x.icon} color={x.color} size={110} />
              <span style={{ fontFamily: SANS, fontSize: 34, fontWeight: 700, color: C.text, whiteSpace: 'nowrap' }}>{x.label}</span>
            </div>
          );
        })}
        <div style={{ position: 'absolute', left: 960, top: 470, transform: `translate(-50%, -50%) scale(${wrench}) rotate(${(1 - ease(f, idea.cue('tools'), idea.cue('tools') + 20, OUT)) * -40}deg)` }}>
          <KdIcon icon="wrench_24_filled" color="gold" size={200} glow={0.6} />
        </div>
      </div>
      {f > idea.to - 6 && <ShortTitle at={idea.to + 2} y={keys(f, [[plan.from - 10, 440], [plan.from + 16, 380]])} />}
      {f > plan.from - 10 && (
        <ShortAgenda y={820} items={[
          { label: t('What it can do'), icon: 'wrench_24_filled', color: 'gold', at: plan.cue('what') },
          { label: t('When it asks'), icon: 'shield_question_24_filled', color: 'orange', at: plan.cue('ask') },
          { label: t('Your own tools'), icon: 'document_24_filled', color: 'teal', at: plan.cue('own') },
        ]} />
      )}
      {tiles.map(x => <Sfx key={x.cue} at={idea.cue(x.cue) - 6} name="pop" volume={0.2} />)}
      <Sfx at={idea.cue('tools') + 2} name="impact" volume={0.22} />
    </AbsoluteFill>
  );
};
