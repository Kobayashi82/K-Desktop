import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { ShortAgenda, ShortTitle } from '../../../src/components/Short';
import { Sfx } from '../../../src/components/Sfx';
import { Showcase } from '../../../src/components/Showcase';
import { ease, IN, keys, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';

/** The three on their test desktop (their real «Try» clips), Kumo then carrying a window; the title and what the video covers. */
export const Hook: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const idea = B('idea');
  const plan = B('plan');
  const away = ease(f, idea.to - 10, idea.to + 10, IN);
  const run = idea.cue('run') - 8;
  const CAST: Array<[string, number, number]> = [['Lander/lander.wave', 330, 0], ['Kobii/kobii.wiggle', 960, 6], ['Kumo/kumo.wave', 1590, 12]];
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', inset: 0, opacity: 1 - away }}>
        {CAST.map(([clip, x, d]) => {
          const k = pop(f, idea.cue('chars') - 10 + d, { damping: 14 });
          const working = clip.startsWith('Kumo') && f >= run;
          return (
            <div key={clip} style={{ position: 'absolute', left: x, top: 470, transform: `translate(-50%, -50%) scale(${(0.7 + 0.3 * k) * (working ? 1.06 : 1)})`, opacity: Math.min(1, k * 1.4) }}>
              {working ? <Showcase clip="Kumo/window.move" width={580} from={run} pause={30} /> : <Showcase clip={clip} width={580} from={idea.cue('chars') - 10 + d} />}
            </div>
          );
        })}
      </div>
      {f > idea.to - 6 && <ShortTitle at={idea.to - 2} y={keys(f, [[plan.from - 10, 440], [plan.from + 16, 380]])} />}
      {f > plan.from - 10 && (
        <ShortAgenda y={820} items={[
          { label: t('Add one'), icon: 'add_circle_24_filled', color: 'green', at: plan.cue('install') },
          { label: t('Talk to it'), icon: 'mic_24_filled', color: 'teal', at: plan.cue('talk') },
          { label: t('How it works'), icon: 'window_24_filled', color: 'blue', at: plan.cue('work') },
          { label: t('Each one, its own'), icon: 'sparkle_24_filled', color: 'purple', at: plan.cue('own') },
        ]} />
      )}
      {CAST.map(([clip, , d]) => <Sfx key={clip} at={idea.cue('chars') - 10 + d} name="pop" volume={0.18} />)}
      <Sfx at={run} name="whoosh-short" volume={0.2} />
    </AbsoluteFill>
  );
};
