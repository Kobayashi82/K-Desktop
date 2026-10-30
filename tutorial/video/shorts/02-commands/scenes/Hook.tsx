import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CommandCard, WORK_MODE } from '../../../src/components/CommandCard';
import { ShortAgenda, ShortTitle } from '../../../src/components/Short';
import { Sfx } from '../../../src/components/Sfx';
import { ease, glide, IN, keys, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';

/** The title, a command assembling itself while it is described, and what the video covers. */
export const Hook: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const idea = B('idea');
  const plan = B('plan');
  const cardAt = idea.cue('command') - 6;
  const card = pop(f, cardAt, { damping: 14, stiffness: 120 });
  const cardOut = ease(f, plan.from - 14, plan.from + 6, IN);
  // One row per few frames while «a list of actions, in order» is said; the hotkey lights it all at the end.
  const rowsAt = WORK_MODE.map((_, i) => cardAt + 18 + i * 12);
  const run = idea.to - 40;
  const active = Math.floor((f - run) / 8);
  return (
    <AbsoluteFill>
      <ShortTitle at={6} y={keys(f, [[cardAt - 10, 440], [cardAt + 14, 250], [plan.from - 10, 250], [plan.from + 16, 380]])} />
      <div style={{
        position: 'absolute', left: 960, top: 690, transform: `translate(-50%, -50%) scale(${(0.8 + 0.2 * card) * (1 - 0.1 * cardOut)}) translateY(${cardOut * 80}px)`,
        opacity: Math.min(1, card * 1.4) * (1 - cardOut),
      }}>
        <CommandCard name={t('Work mode')} rows={WORK_MODE} shown={rowsAt.map(at => glide(f, at, 14))} hotkey={t('Ctrl + Alt + W')}
          width={620} active={f >= run && active < WORK_MODE.length ? active : -1} done={f >= run ? Math.min(active - 1, WORK_MODE.length - 1) : -1} glow={ease(f, run, run + 10) * (1 - ease(f, run + 50, run + 60))} />
      </div>
      {f > plan.from - 10 && (
        <ShortAgenda y={820} items={[
          { label: t('Build one'), icon: 'wrench_24_filled', color: 'gold', at: plan.cue('build') },
          { label: t('Every kind of action'), icon: 'apps_list_24_filled', color: 'blue', at: plan.cue('actions') },
          { label: t('Ways to launch it'), icon: 'keyboard_24_filled', color: 'purple', at: plan.cue('launch') },
        ]} />
      )}
      <Sfx at={cardAt} name="whoosh-short" volume={0.3} />
      {rowsAt.map(at => <Sfx key={at} at={at} name="pop" volume={0.18} />)}
      <Sfx at={run} name="chime" volume={0.22} />
    </AbsoluteFill>
  );
};
