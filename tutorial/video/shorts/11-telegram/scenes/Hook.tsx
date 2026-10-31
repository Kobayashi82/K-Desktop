import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { ShortAgenda, ShortTitle } from '../../../src/components/Short';
import { Sfx } from '../../../src/components/Sfx';
import { ease, IN, keys, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { Chat } from './Chat';

/** A Telegram chat with your PC: a command and a question, then the title and what the video covers. */
export const Hook: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const idea = B('idea');
  const plan = B('plan');
  const win = pop(f, idea.cue('anywhere') - 10, { damping: 15 });
  const away = ease(f, idea.to - 14, idea.to + 8, IN);
  const cmd = idea.cue('command');
  const talk = idea.cue('talk');
  return (
    <AbsoluteFill>
      <Chat width={760} height={660} style={{ left: 580, top: 150, opacity: Math.min(1, win * 1.4) * (1 - away), transform: `scale(${(0.9 + 0.1 * win) * (1 - 0.08 * away)})` }}
        msgs={[
          { at: cmd - 8, me: true, text: '/gaming_mode' },
          { at: cmd + 16, text: '✓ ' + t('Gaming mode') },
          { at: talk - 6, me: true, text: t('Will it rain at home today?') },
          { at: talk + 26, text: t('No rain today: sunny, up to 21 °C.') },
        ]} />
      {f > idea.to - 6 && <ShortTitle at={idea.to - 4} y={keys(f, [[plan.from - 10, 440], [plan.from + 16, 380]])} />}
      {f > plan.from - 10 && (
        <ShortAgenda y={820} items={[
          { label: t('Connect it'), icon: 'send_24_filled', color: 'sky', at: plan.cue('connect') },
          { label: t('Who can write'), icon: 'people_24_filled', color: 'purple', at: plan.cue('who') },
          { label: t('What it can do'), icon: 'flash_24_filled', color: 'gold', at: plan.cue('use') },
        ]} />
      )}
      <Sfx at={idea.cue('anywhere') - 10} name="whoosh-short" volume={0.22} />
      {[cmd - 8, cmd + 16, talk - 6, talk + 26].map(a => <Sfx key={a} at={a} name="pop-high" volume={0.16} />)}
    </AbsoluteFill>
  );
};
