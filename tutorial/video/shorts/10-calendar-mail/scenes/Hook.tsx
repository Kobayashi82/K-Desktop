import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { ShortAgenda, ShortTitle } from '../../../src/components/Short';
import { Sfx } from '../../../src/components/Sfx';
import { ease, IN, keys, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { REMINDER, Toast } from './Notices';

/** A reminder and a mail notice slide in, the title, and what the video covers. */
export const Hook: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const idea = B('idea');
  const plan = B('plan');
  const away = ease(f, idea.to - 14, idea.to + 8, IN);
  const nudge = (at: number) => Math.sin(Math.max(0, Math.min(1, (f - at) / 14)) * Math.PI) * 10;
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', inset: 0, opacity: 1 - away, transform: `translateY(${-away * 40}px)` }}>
        <Toast {...REMINDER} icon="calendar_clock_24_filled" color="blue" show={pop(f, idea.cue('appointments') - 8, { damping: 15 })}
          scale={1.5} style={{ left: 675, top: 170, marginLeft: nudge(idea.cue('reminds')) }} />
        <Toast title={t('Ana')} text={t('Invoice for October')} icon="mail_24_filled" color="blue"
          show={pop(f, idea.cue('mail') - 8, { damping: 15 })} scale={1.5} style={{ left: 675, top: 520, marginLeft: nudge(idea.cue('reminds') + 6) }} />
      </div>
      {f > idea.to - 6 && <ShortTitle at={idea.to - 4} y={keys(f, [[plan.from - 10, 440], [plan.from + 16, 380]])} />}
      {f > plan.from - 10 && (
        <ShortAgenda y={820} items={[
          { label: t('The Agenda'), icon: 'calendar_ltr_24_filled', color: 'orange', at: plan.cue('agenda') },
          { label: t('Your calendars'), icon: 'calendar_sync_24_filled', color: 'blue', at: plan.cue('connect') },
          { label: t('Your mail'), icon: 'mail_24_filled', color: 'red', at: plan.cue('inbox') },
        ]} />
      )}
      <Sfx at={idea.cue('appointments') - 8} name="pop" volume={0.22} />
      <Sfx at={idea.cue('mail') - 8} name="pop-high" volume={0.2} />
      <Sfx at={idea.cue('reminds')} name="chime" volume={0.2} />
    </AbsoluteFill>
  );
};
